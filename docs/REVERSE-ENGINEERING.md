# GCC 主板灯光功能拆分分析

日期：2026-10-19，Asia/Shanghai。目标：本机 B650M AORUS ELITE AX ICE 的 RGB/ARGB 接口。交付：独立 WinForms GUI 和 CLI，可直接通过 Windows HID 使用已核实的主板灯效协议。

## 结论

官方 GCC 的灯光路径可以从外层 UI、同步与设备管理中分离。本项目重实现已核实的 HID 协议，运行时不加载厂商 DLL。本机已目视确认全选五路后的红色响应，以及修正后 0% 的全部熄灯。单接口映射、非零亮度和动态效果仍需逐项目视验收。

[官方 GCC 页面](https://www.gigabyte.com/tw/Consumer/Software/GIGABYTE-Control-Center/global/)说明 RGB Fusion 是通过 Update Center 安装的模块。分析用包为官方 `GCC_26.09.10.01.zip`，其中主板模块版本 `26.08.27.01`，同步模块 `26.08.31.01`。完整大包没有下载完成：研究脚本根据 ZIP/NSIS 结构定位后，使用 HTTP Range 提取两个灯光子安装器。不能把子包 SHA-256 当成整个 GCC ZIP 的 SHA-256。

子包与模块的获取、解包命令、哈希和详细静态分析在 `research/gcc/`，原始材料不进入发行包。

## 真实组件边界

静态证据支持以下路径：

```text
GBT_rgbMotherboard_UC.dll    WPF 页面
        ↓
MB_RGB_Capability.dll       设备/能力入口
        ↓
RgbMotherboard.dll          主板与 IT8297_5702.MCU_8297 协议实现
        ↓
RgbCommon.dll              InvkGHidApi 托管封装
        ↓ P/Invoke
GHidApi.dll                 原生连接、Feature I/O
        ↓
Windows HID                本机 ITE 048D:5702，COL02
```

这是基于程序集元数据、CIL、ILSpy 反编译和原生静态反汇编建立的调用关系；没有运行 GCC 安装器或加载厂商 DLL，也没有声称捕获了 GCC 运行时 USB 流量。其他硬件分支可能使用 SMBus 等接口，本项目没有复用这些分支。

已核实的厂商方法：

| 方法 | 静态位置 | 事实 |
| --- | --- | --- |
| `RGB_Motherboard.ConnectMCU` | token `060000F2`, RVA `C7CC` | 5702 分支连接 `048D:5702`、usage page `FF89`、usage `CC` |
| `MCU_8297.GetMcuInfo` | token `060001E1`, RVA `1D8B8` | 64 字节，`CC 60` 信息请求 |
| `MCU_8297.write_to_mcu` | token `060001EE`, RVA `1E4BC` | `CC` + `20+zone`，参数从 offset 2 起 |
| `MCU_8297.Apply` | token `060001C1`, RVA `1B7F0` | `CC 28` + 分区掩码 |

厂商 `GHidApi.ReadDataFromDevice` 是一次发送请求再读取回复的事务。“查询不改灯效”不等于 USB 总线上完全没有 OUT 传输。

不直接实例化厂商 `RGB_Motherboard` 还有明确原因：其初始化会访问 `InvkSMBCtrl` 并调用原生 `SMBCtrl.dll` 的初始化，读取 SMB/BIOS、LED ID、电源状态，可能更新 `usdata2.xml`；还会写 HKLM 下的 MCU 版本信息，固件支持时通过 `CC47 00` 修改持久化选项。`GetMcuInfo` 后的解析路径还可发送 `CC34` 设置灯珠数量档位，上层应用可先清零全区参数。完整构造流程不能等同为无副作用的灯光 API。本客户端只实现所需 HID 查询、选区灯效和显式引擎切换。实测数量字段本来就为零档（32），尚无证据需要重复设置或全区重置，未据此追加试写。

## REA 证据

按 `reverse-engineer-anything` 技能执行。当前会话没有 REA MCP 注册，CLI 最初受系统 Node 版本和一个不完整 npm 缓存影响；使用机器上已有的 Node 24.19.0 与 REA 6.7.0 公共启动器后完成分析。没有修改 Codex/REA 配置，也没有安装原生反编译引擎。

完整 canonical JSON：

| 文件（相对工作区） | Evidence ID |
| --- | --- |
| `research/gcc/rea/RgbMotherboard.artifact.json` | `ev_87139eca8709e108c3123961a0b47f04c172a82fcd15519ce6e8a8bc1f1a85c5` |
| `research/gcc/rea/RgbMotherboard.members.json` | `ev_9b658ecad589769be8924f224fb26d11e394b5613169d9e2c50edb033e76e056` |
| `research/gcc/rea/RgbCommon.artifact.json` | `ev_28808aa37f11e0238fbc37cc18aa476b700a2185146839ebdb3b92da54dced9c` |
| `research/gcc/rea/RgbCommon.native-boundaries.json` | `ev_015875de1051890ef308951329e9f5fc21a462ee94445ab593023312bd4f2252` |

`RgbMotherboard.dll` SHA-256：`6e4d80a63878c73aa0cd5ff34ebe30ee52f6d9eb868b9eb2de8719bffb51e22e`。REA 分类为 .NET Framework、CIL、AnyCPU（PE machine 为 x86 不等于程序必须以 32 位运行）。

REA 的这些记录属于静态元数据分析，不等于恢复原始源码或执行厂商程序集。`research/gcc/decompiled/` 的 ILSpy 输出和 `metadata/` 的自有 PE/CIL 工具结果是另外的证据来源。

## 本机硬件与报文

`research/local/hardware.json`、`hid-inventory.json`、`hid-information.json` 保留本机观察：

- 主板 WMI：Gigabyte Technology Co., Ltd. / B650M AORUS ELITE AX ICE。
- COL01：usage `0010`，feature 长度 17；不用于本客户端。
- COL02：usage page `FF89`，usage `00CC`，feature 长度 **64，已包括 report ID**。
- `CC60` 查询回复：设备编号 0、固件 `4.0.31.2`、字符串 `IT5701-GIGABYTE V4.0.31.2`、支持标志 0。PID 5702 与产品字符串 IT5701 并不冲突。

厂商分析与 [OpenRGB 固定提交](https://github.com/CalcProgrammer1/OpenRGB/tree/e6a96c65b15caa28d4b3fdb8c5fc4d89761b9357/Controllers/GigabyteRGBFusion2USBController)相互印证。OpenRGB 精确板型配置为 `it5702_31`，layout ID `0860015F`。物理接口数量由[官方规格](https://www.gigabyte.com/Motherboard/B650M-AORUS-ELITE-AX-ICE-rev-1x/sp)和[官方手册第 21–22 页](https://download.gigabyte.com/FileList/Manual/mb_manual_b650m-aorus-elite-ax-ice_1102_e.pdf)确认。

| 物理接口 | Channel | 灯效命令 | 掩码 |
| --- | --- | --- | --- |
| D_LED1 | 5 | `25` | `20` |
| D_LED2 | 6 | `26` | `40` |
| LED_C1 | 1 | `21` | `02` |
| CPU_LED | 2 | `22` | `04` |
| LED_C2 | 4 | `24` | `10` |

全选五个接口为 `76`，不使用泛化的 `FF`，也不将共享配置中的 Chipset Accent 当作外部接头。

灯效报文共 64 字节：offset 0=`CC`，1=命令，2–5=LE32 分区掩码，6–10=0，11=模式，12=亮度，13=最小亮度，14–17=`B,G,R,00`，18–21=第二颜色，22–29=四个 LE16 周期，30–33=效果参数。其余清零。提交报文是 `CC 28` + LE32 选区掩码。

产品实现选择：

- 关闭和任何效果的 UI **0%** 均使用 **static + 黑色 + MaxBrightness=255**。这是 GCC `SetLedOff` → `SetLedEffect(Static, 0, 0, 9, ...)` 经亮度档位钳制后生成的组合；黑色的 RGB 三通道均为零，255 不会将灯点亮。0.1.1 已实机验证这一路径。不把未证实的 mode 0 当关闭命令，也不依赖单独写零亮度。
- 非零常亮/闪烁/循环亮度连续映射至 1–255；非零呼吸映射至 1–100，保留控制器亮度上限。这是当前编码策略，非零亮度变化尚未目视验证。
- 速度 UI 的 1/10 对应厂商慢端/快端。周期采用实际随包 `RGB_Sync_Control/Sync/sp.xml` 的 IT8297 十档表，而非 DLL 中可被覆盖的 fallback。呼吸第三段为 800/700/500/500/450/400/350/300/250/200ms；闪烁间隔为 2400 到 600ms、每档差 200ms。循环保留原表第 6→7 档的非单调值，不擅自调整。
- `sp.xml` SHA-256 为 `3560cfd03bdef6c4bd9b19edae98ab79fa5054c34451756c257e3f6836e5428e`，原表和提取结果在 `research/gcc/extracted/GBT_RGB_Sync_Control_26.08.31.01/0333_sp.xml` 与 `metadata/IT8297.shipped-timing-tables.json`。客户端的连续亮度（尤其呼吸 0–100）是产品扩展；厂商该呼吸路径固定亮度 100，客户端动态效果尚未目视实测。
- 普通 Wave 被此板型能力掩码排除，因此不在界面提供。
- DLED 的 GRB 校准只与逐灯 Direct 数据相关，硬件效果颜色仍按 BGR 写入。
- `CC32` 是整个 ARGB 引擎状态字节，没有找到读取状态或原子改一位的接口。普通 Apply 不发送它；仅“启用硬件灯效”明确发送 `CC31 00` 和 `CC32 00`，作用于两条 ARGB。

详尽字段与源码行号在 `research/openrgb/PROTOCOL-NOTES.md`，原文件哈希在 `SOURCE-MANIFEST.json`。

## 实机验证与未完成的硬件验收

独立 `FKGCC.Cli.exe probe` 返回受支持、固件 4.0.31.2、无冲突进程，原文存于 `research/local/client-probe.json`。本机验证按以下顺序进行；各次 API 均返回成功，但结果以用户观察为准。

| 测试 | 操作 | 用户目视结果 |
| --- | --- | --- |
| 1 | D_LED1，25% 红色常亮 | 没有变化 |
| 2 | `CC31 00` + `CC32 00`，再设置 D_LED1/D_LED2 为 25% 红色 | 仍然没有变化 |
| 3 | 用户确认之前 GCC 能控制，要求测试 0%；全选五路，红色、MaxBrightness=0 | 已变红，没有熄灭 |
| 4 | 修正客户端，0% 改为常亮黑色、MaxBrightness=255；同样全选五路 | **全部熄灭** |

前两次的报文计划与结果位于 `hardware-test-plan.json`、`hardware-test-result.json`、`take-control-result.json` 和 `hardware-test-argb-result.json`。后两次记录在 `hardware-test-zero-*.json`，完整用户观察账本为 `research/local/visual-acceptance.json`。

第 3 次说明灯光实际收到控制，同时暴露出“亮度字段为 0 等于关闭”的错误假设。第 4 次采用 GCC 已知关灯路径修正，用户确认成功，并加入自动回归检查覆盖所有模式的 0%。不据此推断零值的固件内部含义；前两次无变化是否由接线位置、单接口映射或其他状态造成，仍未证实。最终保留用户确认的熄灯状态，没有继续试写。

发送成功仅表示 Windows HID 接受请求；第 3、4 次另外取得用户对实际红色及熄灯的确认。当前没有灯具状态读回协议，也不能可靠读取并恢复此前由其他软件配置的完整场景。五个接口的单独映射、非零亮度、各种动态灯效、睡眠恢复、断电保持均未被实测确认。

范围清单：官方模块拆分 **已回答**；接口/报文 **已交叉核对**；独立客户端 **已构建，实际颜色响应和关灯已确认**；所有接口与效果组合 **未完整验收**；逐灯/Gen2/其他设备 **不在本次约定范围**。
