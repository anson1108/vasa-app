# 本次交付验证记录

1.2 增量：界面已改为 Vue 3 + Element Plus 2.14.6，使用组件库开关、按钮、卡片、表单、加载图标和成功提示。Vue DNS 加载动画、成功/失败反馈、AnyConnect 提示与同步期间刷新频率已更新。前端构建及 3 项状态测试通过，产物已同步至 Android 工程。状态测试覆盖过期状态、停机、失败不显示成功，以及等待时显示加载、成功时显示 AnyConnect 提示。未进行新版界面真机动画或 AnyConnect 连通性验证。

日期：2026-09-26。所有自动验证均为本机或离线验证，没有使用真实 AWS 凭据。

| 项目 | 结果 |
| --- | --- |
| Vue / Vite production build | 成功 |
| 前端状态单元测试（含 DDNS） | 3 / 3 通过 |
| npm production dependency audit | 0 vulnerabilities（当次查询） |
| .NET Release 解决方案构建 | 成功，0 警告，0 错误 |
| .NET 协议、状态与 DNS 测试 | 36 / 36 通过 |
| 本地 ASP.NET HTTP 冒烟 | 4 / 4 通过 |
| CloudFormation YAML / 资源依赖图 | 解析通过，无资源循环依赖；不是云端部署验证 |
| Lambda 权限静态检查 | 固定实例、无终止权限、仅允许写 nonce |
| PowerShell 部署 / 设备脚本 | 语法解析通过；未执行真实部署 |
| 浏览器页面 | 已打开并确认单开关与模拟提示 |
| Android APK 构建 / Keystore 真机 | 用户本机脚本生成 APK；v2 签名验证通过；未做真机验证 |
| AWS SAM 部署 / Lambda 冷启动 | 未执行 |
| IAM 权限、DynamoDB 事务实测 | 未执行；部署后需验证 |
| EC2 实际启停 | 未执行 |
| 阿里云 RecordId / RAM / DNS 更新 | 未在云端核验或执行 |

DDNS 增量：独立 HMAC 测试向量、A 记录更新后核验、IP 不变不写入、无地址等待、停机不修改、域名/主机/类型/线路不匹配拒绝、禁用记录拒绝、实例 IP 变化阻止旧值写入、更新未生效报错、后续重试恢复、私网与 IPv6 地址过滤。新增后台项目已编译；Android 原生桥接已更新并同步 Vue 构建产物，APK 现已由用户本机脚本生成并验证签名。

工具环境：Node 24.11.1、.NET SDK 10.0.100、.NET 8 运行时。NuGet 的常规还原在本沙箱遇到 TLS / 用户目录访问限制；从官方 NuGet 源下载包至本地临时源后完成还原及构建，未关闭证书校验。项目交付配置仍使用官方 nuget.org。2026-09-27：已安装 Microsoft OpenJDK 21.0.12.1、SDK Platform 35、Build-Tools 34.0.0。Gradle 编译因 Java toRealPath / ZipFileSystem.close 的 AccessDeniedException 失败；额外目录授权后，Gradle Wrapper 的缓存锁文件仍被拒绝访问。该次沙箱尝试没有产出 APK；后续本机脚本已成功，见下方更新。

后端测试涵盖正常签名、正文篡改、跨部署 audience、过期/未来时间、无效签名、未知/撤销设备、读后撤销、重复 nonce、同 nonce 多请求、nonce 保留期、启动、停止、目标状态幂等、两类过渡状态、两类终止状态、未知状态、非法动作、实例覆盖、重复 JSON 字段、拒绝 P-384。

后端 HTTP 冒烟：`POST /control` 缺少签名返回 401；`POST /control?x=1` 返回 404；`GET /control` 返回 405；`POST /register` 返回 404。这些请求均在访问 AWS 前被拒绝。

部署后真机验收建议：注册手机；查询状态；启动并观察 pending→running；停止并观察 stopping→stopped；操作期间断网后恢复并刷新；校正手机时间；撤销设备后确认操作被拒绝。实例账单、系统服务就绪状态及手机厂商 Keystore 行为不属于离线测试可以证明的内容。


## 2026-09-27 APK 交付验证

用户在普通本机 PowerShell 执行编译脚本后，已产出 artifacts/ec2-switch-1.2-debug.apk（9,427,119 字节）。apksigner verify 验证通过，APK v2 签名有效；aapt 确认包名 net.personal.ec2switch、版本 1.2 / versionCode 3、minSdk 30、targetSdk 35。此前沙箱编译失败记录保留为环境诊断历史，不代表本机脚本最终结果。尚未进行手机安装、Keystore 真机或真实云端联调。

SHA256: B43D0B2E006416E3B585ADAE43403A8A3321ED52C75ACEFC7E9A78CAFDBD5C88


2026-09-27 部署校验修复：DnsSecretArn 为空时移除 Secrets Manager 权限语句，非空时仅授权该 ARN；保留启用 DDNS 必须提供 ARN 的规则。AWS SAM CLI 1.166.2 的 sam validate --lint 已通过（退出码 0），未执行云端部署。

CI/CD 配置：新增 GitHub main → CodeBuild 测试构建 → 人工审批 → 马来西亚 SAM 部署。两个 CloudFormation 模板通过 SAM CLI 1.166.2 lint；两个 buildspec YAML 解析及命令类型检查通过；3 项部署保护测试通过；PowerShell 配置脚本语法通过；客户托管策略大小检查通过。未创建 AWS 流水线，未完成 GitHub CodeConnections 授权，未执行真实云构建或部署。
