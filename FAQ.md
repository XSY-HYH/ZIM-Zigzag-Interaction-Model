## FAQ - Please Read Before Opening an Issue

### Q: Why not use TLS/HTTPS?
A: Because my use case is frontend pages, which can't handle TLS. Certificate management is also a burden.

### Q: Where's the code?
A: Protocol documentation comes first. For implementation, refer to the original project [Repository-File-Server](https://github.com/XSY-HYH/Repository/blob/main/Services/ChapAuthService.cs). Contributions are welcome.

### Q: Is the name a ripoff?
A: CHAP = Chain Hash Authentication Protocol. Just a coincidence. You can also call it the ZIM protocol.

### Q: Rolling your own protocol?
A: Yes, I was bored. But AES is a standard algorithm — no new encryption was invented.

### Q: No formal verification?
A: You're right. Pull requests or donations to fund an audit are welcome.

### Q: The server is stateful and can't be distributed?
A: This isn't designed for Google. It's for frontend, low-performance, or embedded use cases that still need secure sessions.

### Q: What about DoS protection?
A: Do you think TLS protects against SYN flood?
