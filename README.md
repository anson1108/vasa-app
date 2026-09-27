# Android EC2 单开关 App

准备安装使用时，先看 [配置清单](docs/CONFIGURATION.md)，区分已经预设的参数、后端配置和手机首次配置。

**1.2 界面更新：Vue 3 + Element Plus。** 开关、按钮、卡片、表单、输入框与状态提示均使用 Element Plus（Element UI 的 Vue 3 对应组件库）。DNS 更新期间显示旋转加载图标；运行中的实例经后台确认记录同步成功后，显示绿色成功提示、“DNS 已更新成功”和打开 AnyConnect 的说明。失败、网络不可用、停机或状态过期时不显示成功。同步期间前台每约 5 秒刷新，成功后恢复约 20 秒刷新。

**新版功能：已加入 `my.contoso1.asia` 的阿里云 DDNS 后台同步。** EC2 启动后更新指定 A 记录（默认 ID `2103041539206785024`），失败每分钟重试；手机关闭 App 不影响同步。默认关闭该功能，需配置保存在 AWS Secrets Manager 的专用阿里云 RAM 密钥后启用。详见 [阿里云 DDNS 配置](docs/ALIYUN-DDNS.md)。尚未连接或修改线上 DNS。

Vue 3 前端 + ASP.NET Core 8 后端，前后端分离。用 **VS Code** 编辑 Vue，用 **Visual Studio 2022 17.8+ / 支持 .NET 8 的 Visual Studio** 打开 `Ec2Switch.sln`。Vue 通过 Capacitor 7 打包成 Android App，设备签名由 Java 原生插件完成。

**日常使用：打开 App → 自动读取真实状态 → 点击开关。没有登录页，不输入密码，不要求指纹。** 首次安装需要一次性填写 API 地址并由本人在可信电脑上注册手机公钥；之后无需登录。重装、清除 App 数据或换手机需要重新注册。

预设区域：`ap-southeast-5`；实例：`i-056494d14ab6b3dd0`。停止操作只调用 `StopInstances`，不调用 `TerminateInstances`，不强制关机、不跳过系统正常关机。

## 交付与验证状态

- Vue 生产构建成功；3 项界面状态逻辑测试通过。
- ASP.NET Core API、设备管理工具、DDNS 后台与测试项目均 Release 编译成功，0 警告、0 错误；36 项离线协议、状态及 DNS 测试通过。
- 本地真实 ASP.NET HTTP 服务已验证：无签名 401、带查询参数 404、GET 控制路径 405、注册路径 404。
- 浏览器已显示模拟界面；浏览器演示绝不连接 AWS。未做 Android 真机交互测试。
- **没有连接 AWS，没有部署资源，没有启动或停止该实例。** SAM 部署、IAM 和 DynamoDB 事务仍需在你的 AWS 账户实测。
- **APK 已生成**：2026-09-27 用户在本机运行脚本完成构建。`artifacts/ec2-switch-1.2-debug.apk` 为调试测试包，版本 1.2（3），Android 11+；APK v2 签名验证通过。尚未进行手机安装、Keystore 或云端联调验证。
- 工具版本及详细边界见 `VERIFICATION.md`。

## 目录

```text
Ec2Switch.sln                 Visual Studio 后端解决方案
ec2-switch.code-workspace     VS Code 工作区
frontend/                     Vue 3、Vite、Capacitor
  src/                        单开关页面、状态策略、浏览器模拟
  android/                    可编辑 Android 工程、Gradle Wrapper
    app/src/main/java/net/personal/ec2switch/
      DeviceControlPlugin.java   Keystore 密钥、请求签名、HTTPS
backend/
  Ec2Switch.Api/              ASP.NET Core / Lambda 后端
  Ec2Switch.Admin/            公钥注册与设备撤销控制台程序
  Ec2Switch.DnsWorker/        EventBridge 触发的阿里云 DDNS 后台
  Ec2Switch.Tests/            可直接运行的离线测试程序
template.yaml                AWS SAM / CloudFormation 基础设施
tools/                       部署、设备管理脚本及注册权限策略
docs/SECURITY.md              签名协议与安全边界
```

## 1. 本机准备

1. Node.js **22.12+** 或 24 LTS；本次使用 24.11.1。
2. .NET **8 SDK**，用于 Visual Studio、SAM 编译。项目锁定 `net8.0`；不要只安装运行时。本次本地编译使用 .NET SDK 10.0.100 和 .NET 8 目标包。
3. VS Code，可安装 Vue Official；Visual Studio 安装“ASP.NET 和 Web 开发”工作负载。
4. Android 构建：**JDK 21**、Android SDK Platform 35、Build Tools 34.0.0（当前 Android Gradle Plugin 默认版本）、Platform Tools。可使用 Android Studio 的 SDK Manager 安装工具；日常前端开发仍在 VS Code，后端仍在 Visual Studio。
5. AWS CLI v2、AWS SAM CLI；可选 Docker Desktop 用于 `sam build --use-container`。

在工程根目录打开 PowerShell。路径不要放入需管理员才能写入的目录。不要提交 `.aws/credentials`、App 发布签名密钥或其他私密凭据。

## 2. VS Code 前端开发

打开 `ec2-switch.code-workspace`，在终端执行：

```powershell
cd frontend
npm ci
npm run dev
```

访问终端显示的本机地址（通常 `http://127.0.0.1:5173`）。顶部明确标识“浏览器演示 · 模拟状态，不会连接 AWS”；浏览器用模拟状态检查界面，不能替代手机设备认证或真机测试。没有提供 Web 端软件私钥降级方案。

```powershell
npm test
npm run build
npm run android:sync
```

每次修改 Vue 后都要执行 `android:sync`，才能把新页面复制进 APK。Android 工程已包含在仓库中，**不要再运行 `cap add android`**。

## 3. Visual Studio 后端开发

打开根目录 `Ec2Switch.sln`，还原 NuGet 包，选择 Release 构建。也可使用命令行：

```powershell
dotnet restore Ec2Switch.sln
dotnet build Ec2Switch.sln -c Release --no-restore
dotnet run --project backend/Ec2Switch.Tests -c Release
```

`Ec2Switch.Tests` 是控制台测试程序（无额外测试框架依赖），不是 Visual Studio Test Explorer 测试项目；退出码 0 才表示全部通过。

将 `Ec2Switch.Api` 设为启动项目，F5 可启动本地 Kestrel 服务。`Properties/launchSettings.json` 中的 `TABLE_NAME` 和 `AUDIENCE` 为占位值，真实调试需换成部署输出。有效签名的请求会使用本机 AWS 凭据访问真实 DynamoDB/EC2；不要把这种调试当成模拟。无效签名格式在 AWS 调用前即拒绝。

Android 正式插件仅允许马来西亚 API Gateway HTTPS 地址，不能把地址改为 HTTP 本机调试服务器。浏览器演示与后端离线测试用于日常本地开发；设备认证联调在部署后的 HTTPS 接口上进行。

## 4. AWS 身份与部署

### 不需要把 IAM 密钥交给任何人

优先在你的电脑使用 AWS IAM Identity Center / SSO（若账户已配置）：

```powershell
aws configure sso --profile personal
aws sso login --profile personal
aws sts get-caller-identity --profile personal
```

若个人账户没有 SSO，也可以在**本机终端**运行 `aws configure --profile personal` 配置受限 IAM 用户凭据。不要使用 root access key，不要把 access key 或 secret key 发到聊天、放进 Vue、APK、源码或配置仓库。Lambda 运行时使用角色，不依赖你的个人 access key。

部署操作者必须有 CloudFormation、SAM 构件 S3 存储、Lambda、HTTP API Gateway、DynamoDB、CloudWatch Logs、创建本工程 IAM 角色及 `iam:PassRole` 的权限。部署权限与日常设备注册权限不同；模板内的最小权限角色是 **Lambda 的运行角色**，不是万能部署管理员权限策略。

先在 AWS 控制台确认账号正确、Malaysia 区域已启用、指定实例存在且为可停止的 EBS-backed 实例。若实例受 Auto Scaling 管理或启用了停止保护，先确认该管理策略是否允许手动停止；本工程不主动修改这些设置。

```powershell
# 从工程根目录执行；脚本会展示当前 AWS 身份和部署变更确认。
.\tools\deploy.ps1 -Profile personal

# 有 Docker 时可选择：
# .\tools\deploy.ps1 -Profile personal -UseContainer
```

或逐步执行：

```powershell
sam validate --lint --region ap-southeast-5 --profile personal
sam build
sam deploy --guided --stack-name personal-ec2-switch --region ap-southeast-5 --profile personal --capabilities CAPABILITY_IAM
```

保持 InstanceId 默认值，保存配置即可。部署会创建 API、Lambda、DynamoDB、日志组与运行角色，**不会创建、替换或启动/停止 EC2**。如果遇到无 API Gateway authorizer 的提示：本项目在 ASP.NET Core 中完成每个请求的设备验签，不提供公开无验证的 EC2 操作。

记录输出：

- `ApiBaseUrl`：形如 `https://abcdefghij.execute-api.ap-southeast-5.amazonaws.com`。
- `RegistryTable`：手机公钥和已用 nonce 的表名。

API 基础地址不要加 `/control` 或 stage 后缀。本工程使用 `$default` stage。不要只部署后端而自行更换 API 自定义域名：签名 audience 与接口地址绑定，当前插件只允许默认域名。

## 5. 编译 Android APK

配置 `JAVA_HOME` 指向 **JDK 21**，`ANDROID_HOME` 指向 SDK 目录，并确保 Android SDK 所需许可证已由你接受。可在 `frontend/android/local.properties` 写入本机 SDK 路径，文件不应提交：

```properties
sdk.dir=C:/Users/YOUR_NAME/AppData/Local/Android/Sdk
```

```powershell
cd frontend
npm ci
npm run android:sync
cd android
.\gradlew.bat assembleDebug
```

调试 APK：`frontend/android/app/build/outputs/apk/debug/app-debug.apk`。通过 USB 安装：

```powershell
adb install -r app\build\outputs\apk\debug\app-debug.apk
```

最低 Android 11（API 30）。首次构建 Gradle 会联网下载依赖。若提示需要 Java 21，检查 `java -version`、`JAVA_HOME` 和 Gradle JDK，不能使用 Java 8。

长期使用建议安装 **release 签名版本**：在 Android Studio 打开 `frontend/android`，选择 Build → Generate Signed App Bundle or APK → APK，创建并妥善备份你自己的发布 keystore，然后构建 release。工程 release 默认不嵌入签名凭据；`assembleRelease` 单独执行会得到未签名 APK。发布签名 keystore 和手机运行时的设备认证 Keystore 是两套不同的密钥。

以后升级 App 请保持相同 applicationId 和发布签名，使用覆盖安装；换签名需要卸载，卸载会丢失手机认证私钥并要求重新注册。

## 6. 首次注册手机，然后免登录使用

1. 安装并打开 App，进入“设置”。首次启动自动生成 Android Keystore P-256 密钥，不弹登录、密码或指纹页面。
2. 填入 `ApiBaseUrl`，点击“保存并刷新”。此时显示“尚未注册”是正常现象。
3. 长按选中设置页的**完整设备注册 JSON**，复制到可信电脑，保存为 UTF-8 `device.json`。其中只包含版本、设备指纹与公钥，没有私钥。检查手机和电脑 JSON 中 `deviceId` 一致。
4. 在工程根目录执行，将表名换成 `RegistryTable`：

```powershell
.\tools\device.ps1 -Profile personal -Table "YOUR_REGISTRY_TABLE" -Action register -Device ".\device.json"
```

脚本利用本机 AWS CLI 解析凭据，在进程内传给 .NET 管理工具；不将凭据写入文件或打印。若使用 SSO，先运行 `aws sso login --profile personal`。

5. 手机上点击“刷新状态 / 重试”。出现“运行中”或“已停止”后即可使用开关。关闭设置区域；之后每次打开自动识别本机并刷新状态。

同一个设备记录不能重复覆盖注册（条件写入会失败）；这用于避免误改现有授权。已有设备直接刷新即可。撤销后需恢复时，建议清除 App 数据生成新密钥，再检查并注册新指纹。

管理工具实际命令也可单独运行（须在当前进程已有 AWS 凭据时）：

```powershell
dotnet run --project backend/Ec2Switch.Admin -c Release -- register YOUR_REGISTRY_TABLE device.json
```

只负责设备注册的 IAM 身份可使用 `tools/device-admin-policy.json`，替换账户 ID 和表名；该策略只允许操作 `DEVICE#` 项，不需要 EC2 权限。不要把此策略添加给 Lambda；Lambda 本身不应能注册新公钥。

## 7. 丢失手机或取消授权

保存好注册 JSON 中的 deviceId，在可信电脑上执行：

```powershell
.\tools\device.ps1 -Profile personal -Table "YOUR_REGISTRY_TABLE" -Action revoke -Device "64位deviceId"
```

之后该手机的新请求会被拒绝；已经通过认证并正在执行的 AWS 操作不能被撤销回滚。不要只删除 App 期待服务器同步撤销，卸载不会通知后端。

## 8. 实际行为和故障处理

| 状况 | 行为 / 处理 |
| --- | --- |
| `running` / `stopped` | 显示真实状态并启用开关 |
| `pending` / `stopping` | 显示正在启动/停止，禁用开关，每约 5 秒刷新 |
| 正常稳定状态 | App 前台每约 20 秒刷新；返回前台立即刷新 |
| 网络失败 | 标为状态未确认并禁用开关；状态查询最多自动重试 3 次 |
| 开关命令超时 | 不盲目重发命令，可能已被 AWS 接受；自动/手动刷新后再决定下一步 |
| 未注册 / 已撤销 | 用管理工具注册新设备或检查记录，不接受匿名控制 |
| 手机时间不准 | 开启系统自动日期和时间；允许偏差为 ±120 秒 |
| 服务暂不可用 | 查 Lambda 日志与 IAM、实例停止保护、实例当前状态；前端不会假装操作成功 |
| `terminated` / `shutting-down` | 无法操作；App 不能恢复已终止实例 |
| 启停时间较长 | 保持正在切换状态，不假定固定秒数完成；可在 AWS 控制台核实 |

运行状态表示 EC2 实例状态，不代表实例内部应用、端口或健康检查已经就绪。停止 EC2 也不意味着 EBS、Elastic IP 或本工程服务都停止计费；这里没有实现费用管理。

## 9. 安全机制与边界

- 私钥由 Android Keystore 生成，应用 API 无法导出；App 无 AWS 密钥。
- Keystore 每次自动签名，不要求用户再次验证身份。谁能够使用这台已解锁且已授权的手机，谁就能操作此实例；这是“打开就能用”的设备授权模式。
- 优先使用设备提供的 Keystore 能力；设置页显示硬件支持状态，但本项目**没有远程硬件密钥证明**，不能声称每台手机都具有 StrongBox。
- 签名绑定 API 域名、方法、路径、设备指纹、时间、128 位随机 nonce 和原始正文 SHA-256。
- DynamoDB 强一致读取公钥，再通过事务检查设备仍启用并原子插入 nonce；TTL 600 秒只负责清理，重放防护不依赖 TTL 及时删除。
- API 地址公开，但所有控制/状态请求必须验签。没有 App 注册接口、长期共享 token 或绕过认证的管理 API。
- Lambda 只能启动/停止指定实例；`DescribeInstances` 在 AWS 的权限模型下需要 `Resource: "*"`，另限定请求区域。公钥注册只在可信管理端进行。
- HTTPS 使用系统证书校验；不信任所有证书、不关闭主机名校验、不跟随 HTTP 重定向；禁用 App 数据备份。
- 限流为每秒 2 次、突发 5 次，是流量控制，不是费用硬上限。公共接口遭滥用仍可能产生调用开销；可自行配置 AWS Budgets 告警。

协议细节见 `docs/SECURITY.md`。

## 10. 卸载云端组件

确认不再需要 App 后：

```powershell
sam delete --stack-name personal-ec2-switch --region ap-southeast-5 --profile personal
```

此操作删除应用基础设施，不会删除指定 EC2。DynamoDB 表使用 `Retain`，需你确认后单独删除；构件 S3 存储也应检查是否仍有保留文件。

## 官方参考

- [Android Keystore](https://developer.android.com/privacy-and-security/keystore)
- [Capacitor Android 自定义原生代码](https://capacitorjs.com/docs/android/custom-code)
- [AWS Lambda 部署 ASP.NET 应用](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package-asp.html)
- [DynamoDB TTL 与过期记录](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ttl-expired-items.html)
- [EC2 停止实例的方法](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/instance-stop-methods.html)



