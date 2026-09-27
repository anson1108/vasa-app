# GitHub → AWS 自动构建与部署

仓库 `anson1108/vasa-app`，分支 `main`。当前交付的是可部署流水线配置；本地校验不代表 AWS 流水线已创建或运行成功。

## 流程与区域

GitHub 提交 → CodePipeline → CodeBuild 测试及构建 → 人工批准 → CodeBuild 使用 SAM 部署。

- 新加坡 ap-southeast-1：GitHub CodeConnections、CodePipeline、两个 CodeBuild 项目、私有构建产物桶。
- 马来西亚 ap-southeast-5：Lambda 部署包桶、现有应用栈 personal-ec2-switch、EC2、Secrets Manager。流水线不会迁移实例。
- CodeConnections 官方区域列表未列出马来西亚，因此使用新加坡 GitHub 连接：https://docs.aws.amazon.com/general/latest/gr/codeconnections.html

Vue 会通过测试并构建，产物中的 bundle/vue-preview 是浏览器模拟预览，不是可控制 EC2 的网站。这份流水线不生成、发布或自动安装 APK，也没有 Android 发布签名私钥。Vue App 更新仍需重新构建 APK。两个 .NET Lambda 则通过 SAM 更新。

## 1. GitHub 授权

在 AWS 控制台切换新加坡，进入 CodePipeline 的设置/连接（CodeConnections），创建 GitHub 连接，名称可用 vasa-app-github。安装或选择 AWS 的 GitHub App，只授权 anson1108/vasa-app。完成后状态必须是 Available。复制 Connection ARN；不需要把 GitHub 密码或访问令牌写进源码。

## 2. 网页创建基础设施（适合当前操作方式）

必须使用有权限创建 S3、CodeBuild、CodePipeline、CloudWatch Logs、IAM 角色及客户托管策略的管理员身份。之前授予 iamanson 的三个策略用于应用部署，不包含创建这套流水线的权限；不要用它们推断已有流水线管理权限。GitHub 连接也需要管理员完成授权。

1. 马来西亚 CloudFormation → 创建堆栈 → 上传模板 `ci/packages.template.json`，栈名 `vasa-app-packages`。该模板创建私有部署包桶 `vasa-app-packages-492213594399-ap-southeast-5`。等待 CREATE_COMPLETE。
2. 新加坡 CloudFormation → 创建堆栈 → 上传模板 `ci/pipeline.template.json`，栈名 `vasa-app-pipeline`。
3. 参数 ConnectionArn 填第 1 节的 ARN；DnsSecretArn 已预设用户提供的马来西亚 Secret ARN，无需粘贴密钥内容。
4. 保持其余默认值，勾选允许 CloudFormation 创建 IAM 资源，提交。两份模板不要传错区域。
5. 到新加坡 CodePipeline 打开 `vasa-app`，首次执行会拉取 main 并开始测试、构建；正常应停在 ApproveDeployment。

如果已经有管理员 CLI profile，也可从项目根目录运行：

```powershell
.\tools\setup-pipeline.ps1 -Profile YOUR_ADMIN_PROFILE -ConnectionArn '你的新加坡连接ARN'
```

脚本校验账号和连接状态，不创建 AccessKey，不读取阿里云密钥，也不会删除应用栈。它会创建云资源并可能产生 S3、流水线、构建与日志费用。

## 3. 当前 Lambda 配额尚未批准

可以先创建流水线并完成构建，但暂时不要批准 ApproveDeployment。审批有 AWS 平台时限，超时后可重新运行流水线，不必为保持等待而批准。

配额批准后，还需处理当前 `personal-ec2-switch` 的 ROLLBACK_COMPLETE 状态：先在马来西亚 CloudFormation 检查事件和资源列表；由用户确认删除失败应用栈并等待删除完成，或按 AWS 提供的适用恢复方式处理。模板保留 DynamoDB 表，保留表可能继续存在，不能自动清理。不得删除 EC2、Secrets Manager、vasa-app-packages 或新加坡流水线栈。部署脚本遇到失败状态会停止，绝不会擅自删除栈。

随后重新运行流水线，确认源提交与测试结果，再批准部署。开启 DDNS 后，如果目标 EC2 已在运行，后台可能立即更新指定 A 记录。部署结果文件 deployment-outputs.json 包含 ApiBaseUrl、RegistryTable 等输出；用于 App 首次配置和设备注册。

## 权限与交付边界

BuildRole 仅访问流水线产物与构建日志。DeployRole 持有项目部署权限，GitHub 获准的代码在审批后由它执行；必须保护 main 分支及审查 ci/、template.yaml 的改动。人工审批不是不可信代码的沙箱。部署角色可编辑本项目运行角色的内联策略；API Gateway 权限暂覆盖马来西亚 /apis/*。这些是可信部署角色的权限，不是 App 运行时权限或防提权边界。

PipelineRole 只使用指定 GitHub 连接、两个构建项目及产物桶。所有角色使用 AWS 临时凭据；流水线本身没有读取阿里云 Secret 值的权限。桶阻止公开访问、启用加密与版本控制，删除基础设施栈时保留桶。

构建镜像 aws/codebuild/standard:7.0，.NET 8、Node.js 22、Python 3.12；安装 SAM CLI 1.166.2。依赖从官方源下载，首次云构建仍需验证。后端在构建阶段运行现有离线测试，前端执行 npm ci、npm test 和生产构建；部署消费同一次构建产物，不重新拉取 main。

JSON 基础设施由 `node ci/generate-infrastructure.cjs` 生成，复用 tools 下已审核的项目部署权限。改动后重新生成并 lint；不要只修改生成后的 JSON。
