# FKGCC Light 0.1.1 Preview

面向 GIGABYTE B650M AORUS ELITE AX ICE 的独立 Windows x64 RGB/ARGB 客户端，包含中文 GUI、CLI、源码和构建脚本。运行需要 .NET Framework 4.8，不依赖 GCC 或厂商 DLL。

- 修复 0% 亮度未关灯：统一发送 GCC 的常亮黑色指令。
- 本机已目视确认红色响应以及全部熄灯。
- 39 项硬件无关的自动检查通过。
- GitHub Actions 自动测试、编译、打包，并生成 SHA-256 校验文件。

各接口单独映射、非零亮度和动态效果仍待实机验证；CI 不连接灯光硬件。

解压 ZIP 后运行 `FKGCC.Light.exe`。启动只查询设备，点击应用后才修改灯效。

贡献者：**shangwuqinu、ChatGPT（OpenAI）**。
