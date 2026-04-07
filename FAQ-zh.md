## FAQ - 在你开 Issue 之前请先阅读

### Q: 为什么不用 TLS/HTTPS？
A: 因为我的场景是前端页面，跑不动 TLS。证书管理也是负担。

### Q: 代码呢？
A: 协议文档优先。实现参考原项目 [Repository-File-Server](https://github.com/XSY-HYH/Repository/blob/main/Services/ChapAuthService.cs)。欢迎贡献。

### Q: 名字碰瓷？
A: CHAP = Chain Hash Authentication Protocol。巧合。你也可以叫它 ZIM 协议。

### Q: 自己发明协议？
A: 是的，我闲得蛋疼。但 AES 是标准算法，没发明新加密。

### Q: 没有形式化验证？
A: 你说得对。欢迎提交 PR 或捐赠资助审计。

### Q: 服务端有状态，无法分布式？
A: 这不是为 Google 设计的。这是给前端等微型低性能或嵌入式还需要安全会话的。

### Q: 防 DoS 呢？
A: 你猜 TLS 防不防 SYN Flood？
