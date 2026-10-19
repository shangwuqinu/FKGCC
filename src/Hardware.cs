// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Fkgcc
{
    internal interface IFeatureConnection : IDisposable
    {
        void Send(byte[] report);
        byte[] Receive();
    }
    internal sealed class HidCandidate
    {
        public string Path;
        public ushort Vendor, Product, UsagePage, Usage, FeatureLength;
    }
    internal static class HidSelection
    {
        public static bool IsTarget(HidCandidate value)
        {
            return value!=null && !string.IsNullOrEmpty(value.Path) && value.Vendor==0x048D && value.Product==0x5702
                && value.UsagePage==0xFF89 && value.Usage==0xCC && value.FeatureLength==64;
        }
    }
    internal static class PacketExecutor
    {
        public static void Apply(IFeatureConnection connection, LightingSetting setting)
        {
            byte[][] reports=LightingProtocol.Plan(setting);
            foreach(byte[] report in reports) connection.Send(report);
        }
        public static void TakeControl(IFeatureConnection connection)
        {
            // These affect both ARGB engines. They are intentionally not part of ordinary Apply.
            var beat=new byte[64];beat[0]=0xCC;beat[1]=0x31;connection.Send(beat);
            var engines=new byte[64];engines[0]=0xCC;engines[1]=0x32;connection.Send(engines);
            Thread.Sleep(50);
        }
    }
    public sealed class ProbeResult
    {
        public bool IsSupported;
        public string BoardName, Status, FirmwareVersion, ControllerProduct, DevicePath;
        public string[] Conflicts = new string[0];
    }
    public static class LightingClient
    {
        public static ProbeResult Probe()
        {
            var result = new ProbeResult();
            try
            {
                using(var gate=new DeviceGate())
                using(var device=OpenVerified(result))
                {
                    result.IsSupported=true;
                    result.Conflicts=FindConflicts();
                    result.Status=result.Conflicts.Length==0 ? "已识别主板灯光控制器；检测未更改灯效。" : "请先退出正在控制灯光的软件："+string.Join("、",result.Conflicts);
                }
            }
            catch(Exception ex)
            {
                result.IsSupported=false;result.Status=ex.Message;ClientLog.Write("PROBE FAILED "+ex.Message);
            }
            return result;
        }
        public static string Apply(LightingSetting setting)
        {
            LightingProtocol.Plan(setting); // Validate everything before opening hardware.
            using(var gate=new DeviceGate())
            {
                EnsureNoConflict();
                var result=new ProbeResult();
                using(var device=OpenVerified(result))
                {
                    try { PacketExecutor.Apply(device,setting); }
                    catch(Exception ex) { ClientLog.Write("APPLY FAILED "+ex.Message); throw new IOException("发送中断，灯光状态未确认。请重新检测后再应用。"+ex.Message,ex); }
                }
                ClientLog.Write("APPLY accepted: "+string.Join(",",setting.Headers)+" "+setting.Mode+" "+setting.ColorHex);
                return "已向 "+setting.Headers.Length+" 个接口提交灯效；请查看实际灯光。";
            }
        }
        public static string TakeControl()
        {
            using(var gate=new DeviceGate())
            {
                EnsureNoConflict();
                using(var device=OpenVerified(new ProbeResult()))
                {
                    try { PacketExecutor.TakeControl(device); }
                    catch(Exception ex) { ClientLog.Write("TAKEOVER FAILED "+ex.Message);throw new IOException("切换中断，状态未确认。"+ex.Message,ex); }
                }
            }
            return "已将两条 ARGB 切换为硬件灯效并关闭音频同步；请再点“应用灯效”。";
        }
        static HidConnection OpenVerified(ProbeResult result)
        {
            string board=null,manufacturer=null;
            using(var search=new ManagementObjectSearcher("SELECT Product,Manufacturer FROM Win32_BaseBoard"))
            using(var rows=search.Get())
            {
                foreach(ManagementObject row in rows)
                {
                    using(row) { board=Convert.ToString(row["Product"]);manufacturer=Convert.ToString(row["Manufacturer"]); }
                    break;
                }
            }
            result.BoardName=board;
            if(!BoardLayout.Matches(board,manufacturer)) throw new InvalidOperationException("当前版本仅适配 "+BoardLayout.BoardName+"；检测到："+(board??"未知主板"));
            var candidates=HidNative.Enumerate().Where(HidSelection.IsTarget).ToArray();
            if(candidates.Length==0) throw new InvalidOperationException("未找到主板灯光 HID 接口（048D:5702）。请检查设备是否已被 Windows 识别。");
            if(candidates.Length!=1) throw new InvalidOperationException("发现多个匹配的灯光控制器，无法确定目标，未发送灯效。");
            var connection=new HidConnection(candidates[0].Path);
            try
            {
                var query=new byte[64];query[0]=0xCC;query[1]=0x60;
                connection.Send(query); // Information request, no effect/reset/calibration writes.
                ControllerInfo info=LightingProtocol.ParseInfo(connection.Receive());
                if(info.DeviceNumber!=0) throw new InvalidOperationException("控制器编号不匹配，未发送灯效。");
                result.DevicePath=candidates[0].Path;result.FirmwareVersion=info.FirmwareVersion;result.ControllerProduct=info.Product;
                return connection;
            }
            catch { connection.Dispose();throw; }
        }
        static string[] FindConflicts()
        {
            var names=new HashSet<string>(new[]{"GCC","RGBFusion","RGBFusion2","OpenRGB","SignalRgb","SignalRGB"},StringComparer.OrdinalIgnoreCase);
            var conflicts=new List<string>();
            foreach(var process in Process.GetProcesses())
            {
                using(process)
                {
                    try { if(names.Contains(process.ProcessName)) conflicts.Add(process.ProcessName); }
                    catch(InvalidOperationException) { }
                    catch(Win32Exception) { }
                }
            }
            return conflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        static void EnsureNoConflict()
        {
            string[] conflicts=FindConflicts();
            if(conflicts.Length>0) throw new InvalidOperationException("请先退出这些灯光软件再操作："+string.Join("、",conflicts));
        }
    }

    internal sealed class DeviceGate : IDisposable
    {
        readonly Mutex mutex;
        bool owned;
        public DeviceGate()
        {
            mutex=new Mutex(false,"Local\\FKGCC-Light-048D-5702");
            try { owned=mutex.WaitOne(1000); }
            catch(AbandonedMutexException) { owned=true; }
            if(!owned) {mutex.Dispose();throw new IOException("另一个 FKGCC Light 操作尚未完成，请稍后重试。");}
        }
        public void Dispose() { if(owned) {owned=false;mutex.ReleaseMutex();}mutex.Dispose(); }
    }
    internal static class ClientLog
    {
        internal static void Write(string text)
        {
            try
            {
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FKGCC-Light");
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"client.log");
                if(File.Exists(path) && new FileInfo(path).Length>2*1024*1024) File.WriteAllText(path,"Log rotated.\r\n",Encoding.UTF8);
                File.AppendAllText(path,DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine,Encoding.UTF8);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
    internal sealed class HidConnection : IFeatureConnection
    {
        readonly SafeFileHandle handle;
        public HidConnection(string path)
        {
            // Zero desired access is sufficient for feature IOCTLs on this observed collection.
            handle=HidNative.CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid) {int code=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(code,"无法打开灯光控制器。");}
        }
        public void Send(byte[] report)
        {
            if(report==null || report.Length!=64 || report[0]!=0xCC) throw new ArgumentException("HID 报文格式错误。");
            if(!HidNative.HidD_SetFeature(handle,report,report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"控制器拒绝灯光报文。");
            ClientLog.Write("TX accepted "+BitConverter.ToString(report));
        }
        public byte[] Receive()
        {
            var report=new byte[64];report[0]=0xCC;
            if(!HidNative.HidD_GetFeature(handle,report,report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法读取控制器信息。");
            ClientLog.Write("RX "+BitConverter.ToString(report));
            return report;
        }
        public void Dispose() { handle.Dispose(); }
    }
    internal static class HidNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInterface
        {
            public int Size; public Guid ClassGuid; public int Flags; public UIntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Attributes
        {
            public int Size; public ushort Vendor, Product, Version;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Caps
        {
            public ushort Usage,UsagePage,InputLength,OutputLength,FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
            public ushort LinkNodes,InputButtons,InputValues,InputIndices,OutputButtons,OutputValues,OutputIndices,FeatureButtons,FeatureValues,FeatureIndices;
        }
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] buffer,int length);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] buffer,int length);
        [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("setupapi.dll",EntryPoint="SetupDiGetClassDevsW",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr parent,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr info,IntPtr device,ref Guid guid,uint index,ref DeviceInterface data);
        [DllImport("setupapi.dll",EntryPoint="SetupDiGetDeviceInterfaceDetailW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info,ref DeviceInterface data,IntPtr detail,uint size,out uint required,IntPtr device);
        [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);

        internal static HidCandidate[] Enumerate()
        {
            Guid guid;HidD_GetHidGuid(out guid);
            IntPtr info=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,0x12);
            if(info==new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result=new List<HidCandidate>();
            try
            {
                for(uint index=0;;index++)
                {
                    var data=new DeviceInterface {Size=Marshal.SizeOf(typeof(DeviceInterface))};
                    if(!SetupDiEnumDeviceInterfaces(info,IntPtr.Zero,ref guid,index,ref data))
                    {
                        int code=Marshal.GetLastWin32Error();if(code==259) break;throw new Win32Exception(code);
                    }
                    uint required;
                    SetupDiGetDeviceInterfaceDetail(info,ref data,IntPtr.Zero,0,out required,IntPtr.Zero);
                    if(required<6 || required>65536) throw new IOException("HID 路径长度异常。");
                    IntPtr detail=Marshal.AllocHGlobal((int)required);
                    string path;
                    try
                    {
                        Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                        if(!SetupDiGetDeviceInterfaceDetail(info,ref data,detail,required,out required,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                    }
                    finally {Marshal.FreeHGlobal(detail);}
                    if(path==null || path.IndexOf("vid_048d&pid_5702",StringComparison.OrdinalIgnoreCase)<0) continue;
                    using(var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero))
                    {
                        if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(),"已发现控制器，但无法打开 HID 接口。");
                        var attributes=new Attributes {Size=Marshal.SizeOf(typeof(Attributes))};
                        if(!HidD_GetAttributes(handle,ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        IntPtr preparsed;
                        if(!HidD_GetPreparsedData(handle,out preparsed)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        try
                        {
                            Caps caps;
                            if(HidP_GetCaps(preparsed,out caps)!=0x110000) throw new IOException("无法读取 HID 接口能力。");
                            result.Add(new HidCandidate {Path=path,Vendor=attributes.Vendor,Product=attributes.Product,Usage=caps.Usage,UsagePage=caps.UsagePage,FeatureLength=caps.FeatureLength});
                        }
                        finally {HidD_FreePreparsedData(preparsed);}
                    }
                }
            }
            finally {SetupDiDestroyDeviceInfoList(info);}
            return result.ToArray();
        }
    }
}
