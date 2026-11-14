# 备份与恢复

按需模式保留安装文件与客户端登录配置。日常使用直接点击“启动并打开”。恢复原来的自动启动和保活配置，会重新引入对应的后台运行行为。

## 备份在哪里

系统备份在 `%ProgramData%\ATrustSwitch`，目录限制为管理员和 SYSTEM 可写、普通用户可读：

| 文件 | 内容 |
| --- | --- |
| `original-state.txt` | 首次操作时服务启动模式、任务启用状态和执行操作账户的登录启动项 |
| `aTrustDaemonGuarder.xml` | 原任务定义（任务存在时） |
| `eaio_watchdog_daily.xml` | 原任务定义（任务存在时） |
| `original-recovery.txt` | 两项服务原始 `FailureActions` 二进制值的 Base64 记录 |
| `last-exit.txt` | 最近一次退出核验的时间和结果，属于状态记录 |

登录启动项另存于对应用户的 `%LOCALAPPDATA%\ATrustSwitch\original-autostart.txt`。日志也位于该目录。使用其他管理员账户授权时，管理员与桌面用户可能各有一份用户级备份。

备份只在第一次修改相应配置前写入，后续操作保留原始文件。保管好这些本机文件，避免把含本机信息的内容公开到仓库或 Issue。

## 如何恢复

当前版本没有自动还原按钮。先查看本机备份，再按其中记录恢复，不能假设所有机器原来都是自动启动。

在管理员终端中，根据 `original-state.txt` 为每项服务选择原来的模式：`sc.exe config <服务名> start= demand`、`auto` 或 `disabled`。若原来使用延迟自动启动，应另行核对；当前文本备份未记录延迟标志。

在任务计划程序中，将两项任务的启用状态恢复为备份值。工具只禁用任务，原有任务定义仍保留；XML 可用于核对或在任务丢失时重新导入。

服务失败恢复应根据 `original-recovery.txt` 中的原始记录还原。此文件保存的是 Windows 原始二进制结构，不是可直接执行的脚本；请使用理解该结构的恢复工具或由维护人员转换为服务控制管理器配置。不要把其他机器的恢复参数套到本机。

恢复登录启动项时，在对应用户账户下运行以下 PowerShell，读取该用户自己的备份：

```powershell
$savedValue = Get-Content -LiteralPath "$env:LOCALAPPDATA\ATrustSwitch\original-autostart.txt" -Raw
Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'aTrustTray' -Value $savedValue
```

恢复后可通过 Windows 服务、任务计划程序和工具“详情”核对实际状态。再次点击“彻底退出”或“按需使用”会重新应用按需配置。
