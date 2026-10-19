// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
namespace Fkgcc
{
    public static class CliProgram
    {
        public static int Main(string[] args) { Console.OutputEncoding=new UTF8Encoding(false);return Run(args,Console.Out,Console.Error); }
        public static int Run(string[] args,TextWriter output,TextWriter errors)
        {
            try
            {
                if(args.Length==0 || args[0]=="--help" || args[0]=="help") {Help(output);return 0;}
                if(args[0]=="probe")
                {
                    if(args.Length>1 && !(args.Length==2 && args[1]=="--json")) throw new ArgumentException("probe 不接受这些参数。");
                    var result=LightingClient.Probe();Json(output,result);return result.IsSupported?0:4;
                }
                if(args[0]=="take-control")
                {
                    if(args.Length!=1) throw new ArgumentException("take-control 不接受参数。");
                    Json(output,new {Status=LightingClient.TakeControl()});return 0;
                }
                if(args[0]=="profile")
                {
                    if(args.Length<2 || args.Length>3 || (args.Length==3 && args[2]!="--dry-run")) throw new ArgumentException("用法：profile <path.xml> [--dry-run]");
                    var profile=ProfileStore.Load(args[1]);
                    return ApplyOrPlan(profile,args.Length==3,output);
                }
                if(args[0]!="apply") throw new ArgumentException("未知命令："+args[0]);
                var setting=new LightingSetting();bool dry=false;
                var used=new HashSet<string>(StringComparer.Ordinal);
                var allowed=new[]{"--headers","--mode","--color","--brightness","--speed","--dry-run"};
                for(int i=1;i<args.Length;i++)
                {
                    string key=args[i];
                    if(!allowed.Contains(key)) throw new ArgumentException("未知选项："+key);
                    if(!used.Add(key)) throw new ArgumentException("重复选项："+key);
                    if(key=="--dry-run") {dry=true;continue;}
                    if(++i>=args.Length || args[i].StartsWith("--",StringComparison.Ordinal)) throw new ArgumentException(key+" 缺少参数值。");
                    string value=args[i];int number;
                    switch(key)
                    {
                        case "--headers":setting.Headers=value.Split(',').Select(x=>x.Trim()).ToArray();break;
                        case "--color":setting.ColorHex=value;break;
                        case "--brightness":if(!int.TryParse(value,out number))throw new ArgumentException("亮度必须为整数。");setting.BrightnessPercent=number;break;
                        case "--speed":if(!int.TryParse(value,out number))throw new ArgumentException("速度必须为整数。");setting.Speed=number;break;
                        case "--mode":
                            switch(value.ToLowerInvariant())
                            {
                                case "off":setting.Mode=LightingMode.Off;break;
                                case "static":setting.Mode=LightingMode.Static;break;
                                case "breathing":setting.Mode=LightingMode.Breathing;break;
                                case "blink":setting.Mode=LightingMode.Blink;break;
                                case "cycle":setting.Mode=LightingMode.Cycle;break;
                                default:throw new ArgumentException("支持的灯效：off, static, breathing, blink, cycle。");
                            }
                            break;
                    }
                }
                return ApplyOrPlan(setting,dry,output);
            }
            catch(ArgumentException ex) {Json(errors,new{Error=ex.Message});return 2;}
            catch(InvalidDataException ex) {Json(errors,new{Error=ex.Message});return 2;}
            catch(Exception ex) {Json(errors,new{Error=ex.Message});return 3;}
        }
        static int ApplyOrPlan(LightingSetting setting,bool dry,TextWriter output)
        {
            byte[][] reports=LightingProtocol.Plan(setting);
            if(dry) Json(output,new {DryRun=true,Headers=setting.Headers,Mode=setting.Mode.ToString(),Reports=reports.Select(x=>BitConverter.ToString(x).Replace("-"," ")).ToArray()});
            else Json(output,new {Status=LightingClient.Apply(setting)});
            return 0;
        }
        static void Json(TextWriter output,object value) {output.WriteLine(new JavaScriptSerializer().Serialize(value));}
        static void Help(TextWriter output)
        {
            output.WriteLine("FKGCC Light — B650M AORUS ELITE AX ICE");
            output.WriteLine("  FKGCC.Cli.exe probe                       Query identity; does not change lighting");
            output.WriteLine("  FKGCC.Cli.exe apply [options]             Apply only to selected headers");
            output.WriteLine("  FKGCC.Cli.exe take-control                Enable BOTH ARGB effect engines; disable beat");
            output.WriteLine("  FKGCC.Cli.exe profile <file.xml> [--dry-run]");
            output.WriteLine("Options: --headers D_LED1,D_LED2,LED_C1,LED_C2,CPU_LED");
            output.WriteLine("         --mode off|static|breathing|blink|cycle");
            output.WriteLine("         --color #RRGGBB --brightness 0..100 --speed 1..10");
            output.WriteLine("         --dry-run   Print reports without any hardware I/O");
            output.WriteLine("Exit: 0=accepted, 2=invalid input, 3=operation failed, 4=unsupported/unavailable.");
        }
    }
}
