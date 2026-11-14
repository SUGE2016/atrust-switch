# aTrust Switch · aTrust 开关

<img src="assets/icon.png" width="88" alt="aTrust Switch 电源图标">

**让“退出”终于有字面意思。** 一个 Windows 单文件小工具：需要时启动 aTrust，用完关闭服务、保活入口和残留进程，把网络和电脑的控制权交还给用户。

## 先说几句难听的

退出按钮不是许愿池，手动启动也不是五分钟有效的体验券。

用户已经选择退出，后台却靠计划任务、服务失败重启和守护进程轮番“诈尸”；用户把启动方式设成手动，过一会儿又变回自动——这种把用户意愿当耳旁风的设计，就是流氓式的软件行为。我们对此明确、严厉地批评。

“安全”不该成为无视用户选择、持续占用系统、干扰正常网络访问的通行证。需要连接时认真工作，用完后干净退出，这是软件最基本的教养。让用户为了关掉一个客户端，还得排查服务、任务计划和进程树，是产品设计的失职。

aTrust Switch 送给这些后台“复活甲”一句话：**下班了，别给自己续命。**

下面的技术记录说明本项目遇到的具体行为及验证范围：[自动恢复机制与实测记录](docs/findings.md)。

## 怎么用

从 [最新正式版](https://github.com/SUGE2016/atrust-switch/releases/latest) 下载附件 **aTrust开关.exe**，保存后直接运行。图标和界面已嵌入，只需这一个 EXE；运行环境需要 Windows 的 .NET Framework 4.x，以及已安装的 aTrust、EAIO。也可以[从源码构建](#从源码构建)。

每个 Release 附有 `SHA256SUMS.txt`。用 PowerShell 的 `Get-FileHash .\aTrust开关.exe -Algorithm SHA256` 可核对下载文件。程序未做代码签名。

| 操作 | 效果 |
| --- | --- |
| 启动并打开 | 关闭已识别的自动恢复入口，将两项服务设为手动，启动 EAIO 和 aTrust，再以当前用户身份打开客户端 |
| 彻底退出 | 禁用两项保活任务、关闭服务失败恢复、停止两项服务、清理核实路径后的残留进程，并移除当前用户的客户端登录自启 |
| 更改 → 按需使用 | 配置按需启动，保持当前服务运行状态 |
| 更改 → 主服务自动启动 | 仅设置 aTrust 主服务为自动；其他保活设置保持现状 |
| 关闭工具窗口 | 工具结束，不驻留；aTrust 保持当前状态 |

执行服务操作时会请求 Windows 管理员权限。当前版本固定识别以下安装布局，两项服务都必须存在；自定义目录或不同组件布局需要先适配源码：

```text
C:\Program Files (x86)\Sangfor\aTrust
C:\Program Files\sangfor\EAIO
```

“彻底退出”覆盖上述目录中的用户态程序及已识别的保活入口。网络过滤驱动可能继续加载，界面会显示其状态；工具没有卸载驱动功能。开发机在驱动仍加载的情况下恢复了已测试网站的访问。后续厂商升级或组织策略可能改变配置，需要重新核验。

EAIO 可能被其他深信服产品共用；退出 EAIO 会影响依赖它的功能。此版本面向上述已验证的组合安装环境。

## 从源码构建

无需 NuGet、Visual Studio 或额外的 .NET SDK。使用 64 位 Windows PowerShell 5.1，系统需具备 .NET Framework 4.x 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

产物：`bin\aTrust开关.exe`。可直接复制到桌面。

```powershell
# 基础测试：无须安装 aTrust，无须管理员权限
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1

# 集成测试：需要上述安装布局；读取状态，测试同名无关进程保护
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 -Live

# 可选界面检查：前台窗口、鼠标悬停、菜单、最小化和重绘截图
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-render.ps1
```

集成测试只启动并结束自己创建的模拟进程，不启停 aTrust/EAIO。界面检查会移动鼠标，需要空闲的交互桌面；截图需要人工检查。测试报告和截图写入 `artifacts/`，不会纳入 Git。

## 自动发布

推送到 `main` 后，GitHub Actions 会在 Windows 上运行基础测试、编译单文件 EXE，然后创建带提交号的 **预发布版**，附 EXE 和 SHA256 校验文件。每次推送独立发布；失败的测试或编译会阻止发布。预发布版不会替换“最新正式版”下载入口，已发布的旧版本保留。

正式发布时，将 `VERSION`、`src/AssemblyInfo.cs` 和 `src/app.manifest` 的版本号同步更新，提交后创建并推送对应的 `vX.Y.Z` 标签。标签、VERSION 和 EXE 文件版本不一致时，发布会失败。版本标签触发正式 Release，并更新“最新正式版”入口。普通分支和 PR 不触发发布。

CI 不安装厂商客户端，不能代替真实服务启停、网络和界面回归检查。所有自动产物的 Release 说明都包含源码提交及构建记录链接。

## 管理范围与备份

服务限定为 `aTrustService`、`eaio_service`；任务限定为 `\aTrustDaemonGuarder`、`\eaio_watchdog_daily`。任务的可执行文件和参数必须匹配预期。结束进程前会重新检查同一进程句柄对应的实际路径，拒绝目录外的同名程序及含路径重定向的安装目录。

首次操作前保存原始服务、任务和恢复配置。详见[备份与恢复](docs/recovery.md)。日志只写入本机 `%LOCALAPPDATA%\ATrustSwitch`，程序不包含遥测或日志上传功能。提交问题时请先删去日志中的用户名、路径和组织信息。

源码分为 `src/`（应用）、`tests/`（测试）、`scripts/`（构建与图标生成）、`docs/`（机制和恢复说明）。目前的验证来自单台 Windows 开发机，包含真实启停循环及退出后六分钟观察；重启后的行为和更多客户端版本仍待验证。基础测试包含旧操作模型的单元测试，不替代新按需流程的系统集成验证。

## 开源许可

项目代码和原创电源图标采用 [MIT License](LICENSE)。项目为独立工具，与深信服及 aTrust 官方无隶属关系；aTrust 等产品名称属于各自权利人。仓库只包含本工具源码和原创素材，不包含厂商安装包、客户端文件、用户账号或配置备份。
