# EasyXray

EasyXray 是一个 Windows 原生、无常驻 Agent 的 Ubuntu Xray 部署工具。它通过 SSH 管理服务器，支持：

- 入站：VLESS + REALITY、Hysteria2
- 链式出站：REALITY、Hysteria2、WireGuard
- REALITY / HY2 客户端链接与二维码导出
- Xray 自动安装、远端配置校验、失败回滚和服务状态检查

> 本项目只支持 Ubuntu 服务器；它不是代理客户端，也不会在服务器上安装管理面板或后台 Agent。

## 下载与安装

1. 前往本仓库的 [Releases 页面](../../releases)，下载最新版 `EasyXray.exe`。
2. 安装 **.NET 10 Windows Desktop Runtime（x64）**：
   [官方下载页](https://dotnet.microsoft.com/download/dotnet/10.0)。
   请选择 **Windows x64 → Desktop Runtime**，不是 SDK，也不是仅有 Console Runtime。
3. 双击运行 `EasyXray.exe`。

发布包是“框架依赖、单文件、Windows x64”版本，因此只需一个 `EasyXray.exe`，但目标 Windows 电脑必须预先安装上述 Desktop Runtime。

## 快速开始

1. 在“① 服务器”填写服务器 IP、SSH 端口、登录用户名和私钥文件；如使用密码登录，也可填写 SSH 密码。
2. 点击“检测服务器并继续”。首次连接会记录 SSH 主机公钥指纹；以后指纹变化会被视为安全异常。
3. 新手建议点击“极速部署 REALITY”，或进入“② 入站”手动添加 REALITY。
4. 在“③ 出口与部署”选择 Direct（默认）或已添加的下一跳，点击部署。
5. 部署成功后，在结果区复制 VLESS / HY2 链接或扫描二维码导入客户端。

## SSH 与提权要求

EasyXray 会在检测服务器时依次尝试：

1. SSH 用户本身就是 `root`；
2. 免密 `sudo`；
3. sudo 密码；
4. `su` 的 root 密码。

若未单独填写 sudo 密码，程序会用已输入的 SSH 密码验证 sudo。检测界面会明确显示实际使用的提权方式，或提示缺失/被拒绝的凭据。密码只保留在本次运行的内存中，不会写进配置文件。

## 协议选择

### 推荐：REALITY

- 使用 TCP/443；
- 不需要域名或证书；
- 每次部署会刷新 UUID、Short ID 与一组配套的 Target/SNI；
- 适合新手和低资源服务器。

### Hysteria2

- 使用 UDP/443；
- **必须有一个已解析到服务器的域名**；
- 需要公网能够访问 TCP/80，以便 Certbot 申请或续期证书；
- 程序可自动安装 Certbot 并申请证书。

## 常见问题

### Xray 显示未运行

打开“③ 出口与部署”，点击“检查 Xray 服务状态”。程序会读取 systemd 状态并显示最近相关错误。部署过程本身也会自动安装缺失的 Xray、校验配置、重启服务并确认状态。

### 提示无法获得 root 权限

请确认 SSH 用户具备 sudo 权限；若 sudo 需要密码，填写“SSH 密码”或单独填写“sudo 密码”。没有 sudo 的服务器可填写 root 密码作为 `su` 回退方式。

### HY2 证书申请失败

确认域名 A/AAAA 记录已解析到服务器公网 IP，且 TCP/80 未被安全组、防火墙或其他 Web 服务占用。没有域名时请使用 REALITY。

### 客户端链接在哪里

成功部署后，第三步会显示每个用户对应的二维码和链接。可使用“复制 VLESS”“复制 HY2”或“复制全部”。

## 面向开发者的发布命令

```powershell
dotnet publish EasyXray/EasyXray.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o artifacts/publish/win-x64
```

生成文件：`artifacts/publish/win-x64/EasyXray.exe`。

## 安全提示

- 不要将 SSH 私钥、服务器密码、WireGuard 私钥或导出的代理链接提交到 Git 仓库或发送给不可信的人。
- 首次连接时请核对服务器 SSH 指纹；之后若指纹变化，应先确认服务器未被替换或劫持。
- STUN/WebRTC 阻断仅影响经过该代理服务器的流量，不能阻断客户端绕过代理直接发送的流量。
