// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fkgcc;

internal sealed class RecordingHid : IFeatureConnection
{
    public readonly List<byte[]> Reports = new List<byte[]>();
    public int FailAt = -1;
    public void Send(byte[] report)
    {
        if (Reports.Count == FailAt) throw new IOException("simulated unplug");
        Reports.Add((byte[])report.Clone());
    }
    public byte[] Receive() { throw new Exception("Unexpected input read"); }
    public void Dispose() { }
}
internal static class TransportTests
{
    static int failures;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Test(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch(Exception e) { failures++;Console.WriteLine("FAIL "+name+": "+e.GetType().Name+" "+e.Message); }
    }
    static int Main()
    {
        Test("Only the 64-byte CC collection can receive lighting commands",delegate {
            var c=new HidCandidate{Path="device",Vendor=0x048D,Product=0x5702,UsagePage=0xFF89,Usage=0xCC,FeatureLength=64};
            Check(HidSelection.IsTarget(c),"correct collection rejected");
            c.Usage=0x10;c.FeatureLength=17;Check(!HidSelection.IsTarget(c),"COL01 accepted");
            c.Usage=0xCC;c.FeatureLength=65;Check(!HidSelection.IsTarget(c),"wrong report length accepted");
            c.FeatureLength=64;c.Product=0x5711;Check(!HidSelection.IsTarget(c),"other chip accepted");
        });
        Test("All five header changes commit exactly the physical mask",delegate {
            using(var io=new RecordingHid()) {
                PacketExecutor.Apply(io,new LightingSetting());
                Check(io.Reports.Count==6,"wrong packet count");
                Check(io.Reports.Last()[1]==0x28 && io.Reports.Last()[2]==0x76,"wrong commit mask");
                Check(io.Reports.All(x=>x.Length==64 && x[0]==0xCC),"malformed HID packets");
            }
        });
        Test("An unplug during staging never sends commit or later changes",delegate {
            using(var io=new RecordingHid {FailAt=1}) {
                bool threw=false;try{PacketExecutor.Apply(io,new LightingSetting());}catch(IOException){threw=true;}
                Check(threw,"failure hidden");Check(io.Reports.Count==1,"continued sending after failure");
                Check(io.Reports.All(x=>x[1]!=0x28),"committed incomplete transaction");
            }
        });
        Test("Invalid inputs produce no device I/O",delegate {
            using(var io=new RecordingHid()) {
                bool threw=false;try{PacketExecutor.Apply(io,new LightingSetting {ColorHex="invalid"});}catch(ArgumentException){threw=true;}
                Check(threw && io.Reports.Count==0,"invalid input reached device");
            }
        });
        Test("Explicit takeover only disables beat and enables both ARGB engines",delegate {
            using(var io=new RecordingHid()) {
                PacketExecutor.TakeControl(io);
                Check(io.Reports.Count==2,"wrong takeover count");
                Check(io.Reports[0][1]==0x31 && io.Reports[1][1]==0x32,"unexpected command");
                Check(io.Reports.All(x=>x.Skip(2).All(b=>b==0)),"unexpected takeover payload");
            }
        });
        return failures==0?0:1;
    }
}
