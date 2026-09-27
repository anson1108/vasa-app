# 源码仓库与后续发布

本项目源码仓库：https://github.com/anson1108/vasa-app

用户已授权将本项目后续代码更新提交并推送到该仓库。正常更新应先检查远端变更、完成相关验证，然后推送；不强制覆盖远端历史。GitHub 源码更新不代表 AWS 已部署：AWS CI/CD 流水线尚待配置，当前 Lambda 部署因区域并发配额正在等待处理。

仓库包含 Vue 3、.NET、Android 原生桥接、SAM 基础设施、权限策略与文档。密钥、samconfig.toml、本机 SDK 配置、构建缓存、设备注册文件和签名私钥不能提交。APK 在本机 artifacts 目录，未作为源码纳入 Git。

推送后可继续使用 VS Code 和 Visual Studio 编辑本项目。后续自动部署计划采用 GitHub 与 AWS CodePipeline / CodeBuild；尚未创建流水线配置，不能将普通 git push 当作后端发布成功。
