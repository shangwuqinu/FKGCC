// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Linq;
using Fkgcc;

internal static class CoreTests
{
    static int failures;
    static void Test(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + " " + e.Message); }
    }
    static void Equal<T>(T actual, T expected)
    {
        if (!object.Equals(actual, expected)) throw new Exception("Expected " + expected + ", got " + actual);
    }
    static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Invalid input was accepted");
    }
    static LightingSetting Setting()
    {
        return new LightingSetting { Headers = new[] { "D_LED1" }, ColorHex = "#12A4EF", BrightnessPercent = 50, Speed = 10, Mode = LightingMode.Static };
    }
    static string Hex(byte[] value) { return BitConverter.ToString(value).Replace("-", " ").ToLowerInvariant(); }
    static int Main()
    {
        Test("Exact board allowlist accepts observed WMI identity", delegate { Equal(BoardLayout.Matches("B650M AORUS ELITE AX ICE", "Gigabyte Technology Co., Ltd."), true); });
        Test("Similar boards cannot receive this board's channel mapping", delegate { Equal(BoardLayout.Matches("B650M AORUS ELITE AX", "Gigabyte Technology Co., Ltd."), false); });
        Test("A different manufacturer cannot impersonate board by model", delegate { Equal(BoardLayout.Matches(BoardLayout.BoardName, "Example"), false); });
        Test("D_LED1 maps to channel 5 and D_LED2 to 6", delegate { Equal(BoardLayout.Find("D_LED1").Channel, 5); Equal(BoardLayout.Find("D_LED2").Channel, 6); });
        Test("Five physical headers include CPU_LED and exclude accent zones", delegate { Equal(BoardLayout.Find("LED_C1").Channel, 1); Equal(BoardLayout.Find("LED_C2").Channel, 4); Equal(BoardLayout.Find("CPU_LED").Channel, 2); Equal(BoardLayout.Headers.Length, 5); });
        Test("Static packet uses 64 bytes including ID, channel mask and BGR color", delegate {
            byte[] b = LightingProtocol.Effect(5, Setting());
            Equal(b.Length, 64);
            Equal(Hex(b.Take(18).ToArray()), "cc 25 20 00 00 00 00 00 00 00 00 01 80 00 ef a4 12 00");
            Equal(b.Skip(18).All(x => x == 0), true);
        });
        Test("Breathing fastest has two 400ms phases and 200ms hold", delegate {
            var s = Setting(); s.Mode = LightingMode.Breathing;
            Equal(Hex(LightingProtocol.Effect(5,s).Skip(22).Take(8).ToArray()), "90 01 90 01 c8 00 00 00");
        });
        Test("Breathing slowest has two 1600ms phases", delegate {
            var s = Setting(); s.Mode = LightingMode.Breathing; s.Speed = 1;
            Equal(Hex(LightingProtocol.Effect(5,s).Skip(22).Take(4).ToArray()), "40 06 40 06");
        });
        Test("Blink hold interval follows speed without altering color", delegate {
            var s = Setting(); s.Mode = LightingMode.Blink; s.Speed = 1;
            Equal(Hex(LightingProtocol.Effect(5,s).Skip(22).Take(6).ToArray()), "64 00 64 00 60 09");
        });
        Test("Cycle specifies seven colors and independent phase periods", delegate {
            var s = Setting(); s.Mode = LightingMode.Cycle;
            var b = LightingProtocol.Effect(5,s);
            Equal(Hex(b.Skip(22).Take(4).ToArray()), "2c 01 64 00"); Equal(b[30], (byte)7);
        });
        Test("Breathing brightness respects the controller's 100 maximum", delegate {
            var s=Setting();s.Mode=LightingMode.Breathing;s.BrightnessPercent=100;Equal(LightingProtocol.Effect(5,s)[12],(byte)100);
        });
        Test("Wave is absent from this board's capability mask", delegate { var s=Setting();s.Mode=(LightingMode)6;Reject(s.Validate); });
        Test("Off matches the GCC static-black command regardless of selected color", delegate {
            var s=Setting();s.Mode=LightingMode.Off;var b=LightingProtocol.Effect(5,s);
            Equal(Hex(b.Take(18).ToArray()),"cc 25 20 00 00 00 00 00 00 00 00 01 ff 00 00 00 00 00");
            Equal(b.Skip(18).All(x=>x==0),true);
        });
        Test("Zero percent always turns off instead of leaving a colored or cycling output", delegate {
            foreach(LightingMode mode in Enum.GetValues(typeof(LightingMode)))
            {
                var s=Setting();s.Mode=mode;s.BrightnessPercent=0;
                var b=LightingProtocol.Effect(5,s);
                Equal(Hex(b.Take(18).ToArray()),"cc 25 20 00 00 00 00 00 00 00 00 01 ff 00 00 00 00 00");
                Equal(b.Skip(18).All(x=>x==0),true);
                Equal(s.Mode,mode);Equal(s.ColorHex,"#12A4EF");
            }
        });
        Test("Commit targets only selected channels", delegate { Equal(Hex(LightingProtocol.Commit(new[]{1,5,6}).Take(10).ToArray()),"cc 28 62 00 00 00 00 00 00 00"); });
        Test("Invalid color is rejected before a packet is built", delegate { var s=Setting();s.ColorHex="#ABCDEF12";Reject(delegate{LightingProtocol.Plan(s);}); });
        Test("Invalid range, empty and duplicate selections are rejected", delegate {
            var s=Setting();s.BrightnessPercent=101;Reject(s.Validate);
            s=Setting();s.Speed=0;Reject(s.Validate);
            s=Setting();s.Headers=new string[0];Reject(s.Validate);
            s=Setting();s.Headers=new[]{"D_LED1","D_LED1"};Reject(s.Validate);
            s=Setting();s.Headers=new[]{"chipset"};Reject(s.Validate);
        });
        Test("Unknown effect values and nonphysical channels are blocked", delegate {
            var s=Setting();s.Mode=(LightingMode)99;Reject(s.Validate);
            Reject(delegate{LightingProtocol.Effect(3,Setting());});
            Reject(delegate{LightingProtocol.Commit(new[]{3});});
        });
        Test("Plan does not reset controller or persist hardware flash", delegate {
            var s=Setting();s.Headers=new[]{"D_LED1","D_LED2"};var p=LightingProtocol.Plan(s);
            Equal(p.Length,3);Equal(p[0][1],(byte)0x25);Equal(p[1][1],(byte)0x26);Equal(p[2][1],(byte)0x28);Equal(p[2][2],(byte)0x60);
        });
        Test("Observed CC60 reply decodes firmware and device number", delegate {
            string raw="cc 01 00 07 04 00 1f 02 00 00 00 00 49 54 35 37 30 31 2d 47 49 47 41 42 59 54 45 20 56 34 2e 30 2e 33 31 2e 32 00 00 00 00 01 02 00 02 00 01 00 02 00 01 00 00 01 02 00 00 01 01 57 00 00 00 00";
            var info=LightingProtocol.ParseInfo(raw.Split(' ').Select(x=>Convert.ToByte(x,16)).ToArray());
            Equal(info.FirmwareVersion,"4.0.31.2");Equal(info.Product,"IT5701-GIGABYTE V4.0.31.2");Equal(info.DeviceNumber,(byte)0);
        });
        Test("Malformed or unrequested feature data is rejected", delegate {
            Reject(delegate{LightingProtocol.ParseInfo(new byte[64]);});
            Reject(delegate{LightingProtocol.ParseInfo(new byte[17]);});
            var b=new byte[64];b[0]=0xcc;b[1]=3;Reject(delegate{LightingProtocol.ParseInfo(b);});
        });
        Test("Profiles round trip selected headers and settings", delegate {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
            try{var s=Setting();s.Mode=LightingMode.Breathing;ProfileStore.Save(path,s);var got=ProfileStore.Load(path);Equal(got.Headers[0],"D_LED1");Equal(got.ColorHex,"#12A4EF");Equal(got.Mode,LightingMode.Breathing);Equal(got.BrightnessPercent,50);Equal(got.Speed,10);}finally{if(File.Exists(path))File.Delete(path);}
        });
        Test("Corrupt profiles cannot silently fall back to applying defaults", delegate {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
            try{File.WriteAllText(path,"<LightingProfile version=\"1\"><Color>#FFFFFF</Color></LightingProfile>");Reject(delegate{ProfileStore.Load(path);});}finally{if(File.Exists(path))File.Delete(path);}
        });
        Test("Combined enum text cannot silently turn a profile into blinking", delegate {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
            try {ProfileStore.Save(path,Setting());File.WriteAllText(path,File.ReadAllText(path).Replace("<Mode>Static</Mode>","<Mode>Static, Breathing</Mode>"));Reject(delegate{ProfileStore.Load(path);});}finally{if(File.Exists(path))File.Delete(path);}
        });
        Test("A color with trailing newline is not a valid hex value",delegate {var s=Setting();s.ColorHex="#112233\n";Reject(s.Validate);});
        Test("Breathing uses the shipped GCC hold-time table at each speed",delegate {
            int[] holds={800,700,500,500,450,400,350,300,250,200};
            for(int speed=1;speed<=10;speed++){var s=Setting();s.Mode=LightingMode.Breathing;s.Speed=speed;var b=LightingProtocol.Effect(5,s);Equal((int)b[26]+256*b[27],holds[speed-1]);}
        });
        Test("Cycle preserves the official nonmonotonic timing table",delegate {
            int[] first={1400,1150,850,760,550,460,600,500,400,300};
            int[] second={1200,1050,750,660,450,360,400,300,200,100};
            for(int speed=1;speed<=10;speed++){var s=Setting();s.Mode=LightingMode.Cycle;s.Speed=speed;var b=LightingProtocol.Effect(5,s);Equal((int)b[22]+256*b[23],first[speed-1]);Equal((int)b[24]+256*b[25],second[speed-1]);}
        });
        Test("Vendor breathing and flash control bytes are preserved",delegate {
            var s=Setting();s.Mode=LightingMode.Breathing;var b=LightingProtocol.Effect(5,s);Equal(Hex(b.Skip(30).Take(4).ToArray()),"00 01 00 00");
            s.Mode=LightingMode.Blink;b=LightingProtocol.Effect(5,s);Equal(Hex(b.Skip(30).Take(4).ToArray()),"00 01 01 00");
        });
        Console.WriteLine(failures==0?"All core checks passed.":failures+" checks failed.");return failures==0?0:1;
    }
}
