// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Fkgcc
{
    public enum LightingMode { Off = 0, Static = 1, Breathing = 2, Blink = 3, Cycle = 4 }
    public sealed class HeaderDefinition
    {
        public string Key, Label;
        public int Channel;
        public bool Addressable;
    }
    public static class BoardLayout
    {
        public static readonly string BoardName = "B650M AORUS ELITE AX ICE";
        public static HeaderDefinition[] Headers
        {
            get { return new[] {
                new HeaderDefinition { Key="D_LED1", Label="D_LED1  ·  5V ARGB", Channel=5, Addressable=true },
                new HeaderDefinition { Key="D_LED2", Label="D_LED2  ·  5V ARGB", Channel=6, Addressable=true },
                new HeaderDefinition { Key="LED_C1", Label="LED_C1  ·  12V RGB", Channel=1 },
                new HeaderDefinition { Key="LED_C2", Label="LED_C2  ·  12V RGB", Channel=4 },
                new HeaderDefinition { Key="CPU_LED", Label="CPU_LED  ·  12V RGB", Channel=2 }
            }; }
        }
        public static bool Matches(string board, string manufacturer)
        {
            return string.Equals((board ?? "").Trim(), BoardName, StringComparison.OrdinalIgnoreCase)
                && (string.Equals((manufacturer ?? "").Trim(), "Gigabyte Technology Co., Ltd.", StringComparison.OrdinalIgnoreCase)
                    || string.Equals((manufacturer ?? "").Trim(), "GIGABYTE", StringComparison.OrdinalIgnoreCase));
        }
        public static HeaderDefinition Find(string name)
        {
            var header = Headers.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase));
            if (header == null) throw new ArgumentException("不支持的灯光接口：" + name);
            return header;
        }
    }
    public sealed class LightingSetting
    {
        public string[] Headers = new[] { "D_LED1", "D_LED2", "LED_C1", "LED_C2", "CPU_LED" };
        public LightingMode Mode = LightingMode.Static;
        public string ColorHex = "#7C5CFF";
        public int BrightnessPercent = 65;
        public int Speed = 5;
        public void Validate()
        {
            if (Headers == null || Headers.Length == 0) throw new ArgumentException("请至少选择一个灯光接口。");
            if (Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Headers.Length) throw new ArgumentException("接口不能重复。");
            if (!Enum.IsDefined(typeof(LightingMode), Mode)) throw new ArgumentException("不支持此灯效。");
            foreach (string key in Headers)
            {
                BoardLayout.Find(key);
            }
            if (!Regex.IsMatch(ColorHex ?? "", "\\A#?[0-9A-Fa-f]{6}\\z")) throw new ArgumentException("颜色必须为 6 位十六进制，例如 #7C5CFF。");
            if (BrightnessPercent < 0 || BrightnessPercent > 100) throw new ArgumentException("亮度必须为 0–100。");
            if (Speed < 1 || Speed > 10) throw new ArgumentException("速度必须为 1–10。");
        }
    }
    public sealed class ControllerInfo
    {
        public byte DeviceNumber, SupportFlags;
        public string FirmwareVersion, Product, ChipId;
    }
    public static class LightingProtocol
    {
        // GCC RGB_Sync_Control 26.08.31.01, Sync/sp.xml, IT8297, num 0..9.
        // Source file SHA-256: 3560cfd03bdef6c4bd9b19edae98ab79fa5054c34451756c257e3f6836e5428e.
        static readonly int[] BreathingHold = {800,700,500,500,450,400,350,300,250,200};
        static readonly int[] CycleRise = {1400,1150,850,760,550,460,600,500,400,300};
        static readonly int[] CycleFall = {1200,1050,750,660,450,360,400,300,200,100};
        // Protocol facts corroborated against OpenRGB e6a96c65 and the local CC60 reply.
        // Header channels are board-specific. Byte zero already includes HID report ID.
        public static byte[] Effect(int channel, LightingSetting setting)
        {
            if (setting == null) throw new ArgumentException("缺少灯光设置。");
            setting.Validate();
            if (!setting.Headers.Select(x => BoardLayout.Find(x).Channel).Contains(channel)) throw new ArgumentException("通道不在所选物理接口中。");
            var packet = new byte[64];
            packet[0] = 0xCC; packet[1] = (byte)(0x20 + channel); packet[2] = (byte)(1 << channel);
            // This firmware still displayed red with MaxBrightness=0. GCC turns off
            // with static black and MaxBrightness=255; use that for every 0% request.
            if (setting.Mode == LightingMode.Off || setting.BrightnessPercent == 0)
            {
                packet[11] = (byte)LightingMode.Static;
                packet[12] = 255;
                return packet;
            }
            packet[11] = (byte)setting.Mode;
            packet[12] = setting.Mode == LightingMode.Breathing ? (byte)setting.BrightnessPercent : (byte)((setting.BrightnessPercent * 255 + 50) / 100);
            uint color = uint.Parse(setting.ColorHex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            packet[14] = (byte)color; packet[15] = (byte)(color >> 8); packet[16] = (byte)(color >> 16);
            int speed = 10 - setting.Speed;
            switch (setting.Mode)
            {
                case LightingMode.Breathing:
                    int period = speed <= 6 ? 400 + speed * 100 : 1000 + (speed - 6) * 200;
                    Put16(packet,22,period); Put16(packet,24,period); Put16(packet,26,BreathingHold[setting.Speed-1]); packet[31]=1; break;
                case LightingMode.Blink:
                    Put16(packet,22,100); Put16(packet,24,100); Put16(packet,26,speed*200+600); packet[31]=1;packet[32]=1; break;
                case LightingMode.Cycle:
                    Put16(packet,22,CycleRise[setting.Speed-1]); Put16(packet,24,CycleFall[setting.Speed-1]); packet[30]=7; break;
            }
            return packet;
        }
        static void Put16(byte[] buffer, int offset, int value) { buffer[offset]=(byte)value; buffer[offset+1]=(byte)(value>>8); }
        public static byte[] Commit(IEnumerable<int> channels)
        {
            if (channels == null) throw new ArgumentException("缺少通道。");
            var packet = new byte[64]; packet[0]=0xCC; packet[1]=0x28;
            foreach (int channel in channels)
            {
                if (!BoardLayout.Headers.Any(x=>x.Channel==channel)) throw new ArgumentException("不支持此物理通道。");
                packet[2] |= (byte)(1 << channel);
            }
            if (packet[2]==0) throw new ArgumentException("未选择通道。");
            return packet;
        }
        public static byte[][] Plan(LightingSetting setting)
        {
            if (setting == null) throw new ArgumentException("缺少灯光设置。");
            setting.Validate();
            int[] channels = setting.Headers.Select(x=>BoardLayout.Find(x).Channel).ToArray();
            var packets = channels.Select(x=>Effect(x,setting)).ToList();
            packets.Add(Commit(channels));
            return packets.ToArray();
        }
        public static ControllerInfo ParseInfo(byte[] report)
        {
            if (report==null || report.Length!=64 || report[0]!=0xCC || report[1]!=1) throw new InvalidDataException("控制器信息回复格式不匹配。");
            string product = Encoding.ASCII.GetString(report,12,28).TrimEnd('\0',' ');
            if (!product.StartsWith("IT5701-GIGABYTE",StringComparison.Ordinal) && !product.StartsWith("IT5702-GIGABYTE",StringComparison.Ordinal)) throw new InvalidDataException("此控制器不属于已验证的 IT5701/5702 系列。");
            return new ControllerInfo {
                DeviceNumber=report[2], SupportFlags=report[11], Product=product,
                FirmwareVersion=string.Join(".",report.Skip(4).Take(4).Select(x=>x.ToString(CultureInfo.InvariantCulture))),
                ChipId="0x"+BitConverter.ToUInt32(report,56).ToString("X8",CultureInfo.InvariantCulture)
            };
        }
    }
    public static class ProfileStore
    {
        public static void Save(string path, LightingSetting setting)
        {
            if (setting == null) throw new ArgumentException("缺少灯光设置。");
            setting.Validate();
            var doc = new XDocument(new XElement("LightingProfile",new XAttribute("version","1"),new XAttribute("board",BoardLayout.BoardName),
                new XElement("Headers",setting.Headers.Select(x=>new XElement("Header",BoardLayout.Find(x).Key))),
                new XElement("Mode",setting.Mode.ToString()),new XElement("Color","#"+setting.ColorHex.TrimStart('#').ToUpperInvariant()),
                new XElement("Brightness",setting.BrightnessPercent),new XElement("Speed",setting.Speed)));
            string full = Path.GetFullPath(path);
            string temp = full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                doc.Save(temp);
                if (File.Exists(full)) File.Replace(temp,full,null); else File.Move(temp,full);
            }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public static LightingSetting Load(string path)
        {
            if (new FileInfo(path).Length>65536) throw new InvalidDataException("配置文件过大。");
            try
            {
                var options = new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=65536 };
                using (var reader=XmlReader.Create(path,options))
                {
                    var root=XDocument.Load(reader).Root;
                    if(root==null || root.Name!="LightingProfile" || (string)root.Attribute("version")!="1" || (string)root.Attribute("board")!=BoardLayout.BoardName) throw new InvalidDataException("配置文件版本或主板型号不匹配。");
                    if(root.Elements().Count()!=5 || root.Elements("Headers").Count()!=1 || root.Elements("Mode").Count()!=1 || root.Elements("Color").Count()!=1 || root.Elements("Brightness").Count()!=1 || root.Elements("Speed").Count()!=1) throw new InvalidDataException("配置文件字段不完整或重复。");
                    LightingMode mode;
                    int brightness, speed;
                    string modeName=(string)root.Element("Mode");
                    if(!Enum.GetNames(typeof(LightingMode)).Contains(modeName) || !Enum.TryParse(modeName,out mode) || !int.TryParse((string)root.Element("Brightness"),out brightness) || !int.TryParse((string)root.Element("Speed"),out speed)) throw new InvalidDataException("配置文件参数无效。");
                    var value=new LightingSetting { Headers=root.Element("Headers").Elements("Header").Select(x=>x.Value).ToArray(), Mode=mode, ColorHex=(string)root.Element("Color"), BrightnessPercent=brightness, Speed=speed };
                    value.Validate(); return value;
                }
            }
            catch(XmlException ex) { throw new InvalidDataException("配置文件不是有效的灯光配置。",ex); }
        }
    }
}
