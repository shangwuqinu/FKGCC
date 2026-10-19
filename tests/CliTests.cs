// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using Fkgcc;
internal static class CliTests
{
    static int failures;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Test(string name,Action action) {try{action();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static int Main()
    {
        Test("No command displays help without controlling hardware",delegate {
            var o=new StringWriter();Check(CliProgram.Run(new string[0],o,new StringWriter())==0,"help exit code");Check(o.ToString().Contains("probe"),"help missing");
        });
        Test("Dry run produces auditable bytes without device I/O",delegate {
            var o=new StringWriter();var e=new StringWriter();
            Check(CliProgram.Run(new[]{"apply","--headers","D_LED1","--mode","static","--color","#12A4EF","--brightness","50","--dry-run"},o,e)==0,e.ToString());
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(o.ToString());
            Check((bool)data["DryRun"],"must label dry run");
            Check(o.ToString().Contains("CC 25 20") && o.ToString().Contains("CC 28 20"),"wrong selected channel");
            Check(!o.ToString().Contains("CC 32"),"implicit takeover");
        });
        Test("Unknown or duplicate options fail before touching hardware",delegate {
            Check(CliProgram.Run(new[]{"apply","--colour","#FF0000"},new StringWriter(),new StringWriter())==2,"unknown flag accepted");
            Check(CliProgram.Run(new[]{"apply","--color","#FFFFFF","--color","#000000"},new StringWriter(),new StringWriter())==2,"duplicate flag accepted");
        });
        Test("Missing option value and unknown mode are rejected",delegate {
            Check(CliProgram.Run(new[]{"apply","--color"},new StringWriter(),new StringWriter())==2,"missing value accepted");
            Check(CliProgram.Run(new[]{"apply","--mode","rainbow"},new StringWriter(),new StringWriter())==2,"unsupported mode accepted");
        });
        Test("Profile dry run cannot apply a different motherboard's profile",delegate {
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml");
            try {
                File.WriteAllText(path,"<LightingProfile version=\"1\" board=\"Other\" />");
                Check(CliProgram.Run(new[]{"profile",path,"--dry-run"},new StringWriter(),new StringWriter())==2,"wrong board profile accepted");
            } finally {File.Delete(path);}
        });
        return failures==0?0:1;
    }
}
