# CHAP Protocol Family Documentation Index

> **NOTE: This protocol is NOT the legacy Challenge-Handshake Authentication Protocol (CHAP).** This is a completely different protocol named Chain Hash Authentication Protocol.

> **Repository Name Notice:** To prevent repository name conflicts, this repository has been named "Zigzag Interaction Model". Please do not confuse the CHAP protocol described in this project with the legacy Challenge-Handshake Authentication Protocol.

> **Implementation Code Disclaimer|实现代码免责声明**
> 
> This repository includes implementation examples (JavaScript client, C#/Python server) for demonstration purposes ONLY. DO NOT use these implementations directly in production environments. For production use, please follow the CHAP/CHAP-IEM specifications and implement according to your own security requirements.
> 
> 仓库内包含的实现代码（JS 客户端、C#/Python 服务端）仅用于演示！切勿直接用于生产环境。生产环境使用请按照 CHAP/CHAP-IEM 的设计思路自行编写。

> **Implementation Help|工程实现帮助**
> 
> - English: [pih.md](./pih.md)
> - 中文: [pih-zh.md](./pih-zh.md)

> **Before submitting issues, please read the FAQ|提交问题前，请先阅读非人哉问题解答以保护小脑**
> 
> - 中文: [FAQ-zh.md](./FAQ-zh.md)  
> - English: [FAQ.md](./FAQ.md)

> **For AI without image recognition capability|专为非识图AI准备**
> 
> - [Flowchart-code.md](./Flowchart-code.md) - This document contains flowchart source code for AI that does not perform image recognition. If you are a human, you can ignore it.
> - [Flowchart-code.md](./Flowchart-code.md) - 这个文档包含流程图源码，专门提供给无法识别图片的AI阅读。如果你是人，可以忽略此文档。

---

## Project Overview

The CHAP Protocol Family is a collection of lightweight communication protocols designed for connection state management with built-in chain authentication. The core philosophy is derived from the Zigzag Interaction Model (ZIM), where client and server maintain a continuously evolving state through each request-response cycle.

---

## Cryptographic Specifications

### Hash Algorithm

Both CHAP and CHAP-IEM use a hash algorithm to convert the user's secret key into a fixed-length encryption key.

| Parameter | Specification |
|-----------|---------------|
| Algorithm | SHA-256 |
| Output Length | 256 bits (32 bytes) |
| Input | User password / secret key |
| Output | Pre-shared key K |

**Note**: The hash operation is performed only once during the login phase. The resulting value K serves as the pre-shared symmetric key for the initial encrypted exchange.

### Encryption Algorithm

Both CHAP and CHAP-IEM use AES for all encrypted communications.

| Parameter | Specification |
|-----------|---------------|
| Algorithm | AES-256 |
| Mode | Not specified (implementation dependent, recommended: GCM or CBC with proper padding) |
| Key Length | 256 bits |
| Block Size | 128 bits |

**Key Usage**:

| Protocol Phase | CHAP | CHAP-IEM |
|----------------|------|----------|
| Login Phase | AES256_K | AES256_K |
| Operation Phase | AES256_K (key remains K) | AES256_IDn (key changes with each operation) |

---

## Protocol Family Overview

### What is CHAP?

CHAP (Chain Hash Authentication Protocol) is a general-purpose protocol that can adapt to HTTP, HTTPS, TCP, WebSocket, and other transport protocols. Its core design targets connection state management rather than multi-user authentication. The protocol uses pre-shared keys for encryption and maintains a chained ID system where each successful operation destroys the current ID and generates a new one for the next interaction.

**Key features:**
- Pre-shared key authentication (SHA-256 → AES-256)
- Chain-based ID management
- Built-in exception recovery
- Not suitable for large-scale multi-user scenarios

### What is ZIM?

ZIM (Zigzag Interaction Model) is the deeper theoretical framework underlying CHAP. In this model, two consecutive sessions between client and server are always offset by one "tooth" while maintaining a meshed state as a whole. Each request carries the current tooth position, and each response advances to the next position, forming a continuous chain of state transitions.

CHAP is one exemplary implementation of ZIM. Any protocol conforming to this model can be considered a member of the CHAP family.

### What is CHAP-IEM?

CHAP-IEM (ID Encryption Mode) is a derivative variant of standard CHAP. The core difference: standard CHAP always uses the pre-shared key for encryption, while CHAP-IEM switches to using the ID itself as the encryption key after login completion.

**Key differences from standard CHAP:**
- Login phase uses pre-shared key K (same as standard CHAP)
- Subsequent operations use the current ID as the encryption key
- Keys change continuously, providing forward secrecy
- Automatic sync recovery using K (same as standard CHAP) — the pre-shared key K is retained for recovery purposes only, not used for operation encryption

**Cryptographic note for CHAP-IEM**: The ID values used as encryption keys must meet the same security requirements as any AES-256 key. Implementations should ensure IDs have sufficient entropy (at least 256 bits) or apply a KDF (Key Derivation Function) to shorter IDs before using them as encryption keys.

### What is CHAP-IEM-SKN?

CHAP-IEM-SKN (Secure Key Negotiation) is a further enhancement of CHAP-IEM that introduces a pre-shared key mixing exchange mechanism. The core innovation: using the pre-shared key as a "root" to mix with random values, both parties negotiate a session key without exposing the pre-shared secret. This design is inherently resistant to offline brute force attacks.

**Key differences from CHAP-IEM:**
- Login key derived from key exchange instead of direct password hash
- Pre-shared key can be low entropy (e.g., PIN code)
- Offline brute force resistance
- Retains all IEM features (chained keys, forward secrecy, exception recovery)

> **⚠️ IMPLEMENTATION WARNING:** The CHAP-IEM-SKN variant currently has NO implementation examples in this repository. If you intend to use this variant in production, please exercise extreme caution — conduct thorough security reviews, testing, and validation before deployment.

> **⚠️ 实现警告：** CHAP-IEM-SKN 变体目前在本仓库中**没有**实现示例。如果计划在生产环境中使用此变体，请务必谨慎——在部署前进行充分的安全审查、测试和验证。

### What is CHAP-IEM-CSW?

CHAP-IEM-CSW (Concurrent Sliding Window) is an extension of CHAP-IEM that enables **asynchronous concurrent requests** while preserving all security properties of the original protocol.

**Core Problem Solved:**
- Original CHAP-IEM forces strict sequential execution (request N must wait for response N before sending request N+1)
- This blocks HTTP/2 multiplexing, parallel API calls, and any high-concurrency scenario

**Key Innovations:**
- **Multiple logical channels** per session, each with its own independent ID chain
- **Client-generated next IDs** (instead of server-generated), allowing optimistic key updates
- **Sequence numbers + sliding window** for replay protection within each channel
- Full parallelism without sacrificing forward secrecy

> **📖 Detailed Documentation:** For complete specification including channel negotiation, packet format, sliding window maintenance, and exception recovery, please refer to the dedicated document.

### What is CHAP-IEM-AEAD?

CHAP-IEM-AEAD is a security-hardened variant of CHAP-IEM that mandates **authenticated encryption (AEAD)** and introduces **explicit nonce / sequence number management**.

**Problems Solved:**
- Original CHAP-IEM does not specify encryption mode (CBC has padding oracle risks)
- No integrity protection for ciphertext
- No nonce management specification (GCM nonce reuse is catastrophic)
- Single-layer replay protection (ID only)

**Key Innovations:**
- **Mandatory AEAD** (AES-256-GCM or ChaCha20-Poly1305)
- **Explicit 12-byte nonce** with embedded monotonic sequence number
- **Dual replay protection**: ID single-use + sequence number monotonic check
- Guaranteed `(key, nonce)` uniqueness

> **📖 Detailed Documentation:** For complete specification including nonce construction, packet format, and sequence number checking, please refer to the dedicated document.

### What is CHAP-DH?

CHAP-DH is an anonymous variant of CHAP-IEM that removes the login phase and replaces it with Diffie-Hellman key exchange.

**Core Problem Solved:**
- CHAP-IEM requires pre-shared secrets (passwords) and maintains server-side key tables
- This creates configuration overhead for large numbers of clients

**Key Innovations:**
- **Zero pre-shared secrets** — no passwords, no pre-configured keys
- **DH key exchange** establishes session key K
- **Client or server generates ID₁** (instead of server-only)
- **Completely anonymous** — no identity information transmitted
- **Zero server-side key tables** — stateless with respect to client identities

> **📖 Detailed Documentation:** For complete specification including key exchange, ID₁ distribution, and exception recovery, please refer to the dedicated document.

---

## Documentation Navigation

### CHAP Protocol

| Language | Document |
|----------|----------|
| English | [CHAP.md](./CHAP.md) |
| Chinese | [CHAP-zh.md](./CHAP-zh.md) |

### CHAP-IEM Variant

| Language | Document |
|----------|----------|
| English | [CHAP-IEM.md](./CHAP-IEM.md) |
| Chinese | [CHAP-IEM-zh.md](./CHAP-IEM-zh.md) |

### CHAP-IEM-SKN Variant (Secure Key Negotiation)

| Language | Document |
|----------|----------|
| English | [CHAP-IEM-SKN.md](./CHAP-IEM-SKN.md) |
| Chinese | [CHAP-IEM-SKN-zh.md](./CHAP-IEM-SKN-zh.md) |

> **⚠️ Note:** CHAP-IEM-SKN is a theoretical specification. No implementation examples are provided in this repository. Production use requires independent implementation and thorough security validation.

> **⚠️ 注意：** CHAP-IEM-SKN 为理论规范。本仓库未提供实现示例。生产环境使用需要自行实现并进行充分的安全验证。

### CHAP-IEM-CSW Variant (Concurrent Sliding Window)

| Language | Document |
|----------|----------|
| English | [CHAP-IEM-CSW.md](./CHAP‑IEM‑CSW.md) |
| Chinese | [CHAP-IEM-CSW-zh.md](./CHAP‑IEM‑CSW-zh.md) |

> **📖 About CSW:** This extension transforms CHAP-IEM into a protocol suitable for HTTP/2, gRPC, real-time multiplayer games, and modern web APIs where multiple requests must be processed concurrently. It fully preserves forward secrecy, replay protection, and automatic recovery.

> **📖 关于 CSW：** 此扩展将 CHAP-IEM 转变为适用于 HTTP/2、gRPC、实时多人游戏和现代 Web API 的协议，在保持前向安全性、抗重放和自动恢复能力的同时支持并发请求处理。

### CHAP-IEM-AEAD Variant (Authenticated Encryption)

| Language | Document |
|----------|----------|
| English | [CHAP-IEM-AEAD.md](./CHAP‑IEM‑AEAD.md) |
| Chinese | [CHAP-IEM-AEAD-zh.md](./CHAP‑IEM‑AEAD-zh.md) |

> **📖 About AEAD:** This security-hardened variant mandates authenticated encryption (AES-256-GCM or ChaCha20-Poly1305) and explicit nonce management, eliminating CBC padding oracle risks and GCM nonce reuse vulnerabilities while adding dual-layer replay protection.

> **📖 关于 AEAD：** 此安全强化变体强制使用认证加密（AES-256-GCM 或 ChaCha20-Poly1305）和显式 nonce 管理，消除 CBC 填充预言机风险和 GCM nonce 重用漏洞，同时增加双层抗重放保护。

### CHAP-DH Variant (Anonymous DH)

| Language | Document |
|----------|----------|
| English | [CHAP-DH.md](./CHAP-DH.md) |
| Chinese | [CHAP-DH-zh.md](./CHAP-DH-zh.md) |

> **📖 About CHAP-DH:** This variant removes all authentication and pre-shared secrets, using standard DH key exchange to establish a session key while retaining the IEM chain for forward secrecy and bidirectional communication. Ideal for IoT, anonymous access, and zero-configuration scenarios.

> **📖 关于 CHAP-DH：** 此变体移除所有身份验证和预共享秘密，使用标准 DH 密钥交换建立会话密钥，同时保留 IEM 链以实现前向安全性和双向通信。适用于 IoT、匿名访问和零配置场景。

### Bidirectional Communication

| Language | Document |
|----------|----------|
| English | [BCPF.md](./BCPF.md) |
| Chinese | [BCPF-zh.md](./BCPF-zh.md) |

> **📖 About BCPF:** This document discusses server-initiated push messaging and bidirectional communication support across the CHAP protocol family, covering CHAP, CHAP-IEM, and CHAP-IEM-SKN.

> **📖 关于 BCPF：** 本文档讨论 CHAP 协议家族的服务端主动推送和双向通信支持，涵盖 CHAP、CHAP-IEM 和 CHAP-IEM-SKN。

---

## Quick Comparison

| Feature | CHAP | CHAP-IEM | CHAP-IEM-SKN | CHAP-IEM-CSW | CHAP-IEM-AEAD | CHAP-DH |
|---------|------|----------|--------------|--------------|---------------|---------|
| Encryption Key | Fixed K | Switches from K to ID | Switches from K_session to ID | Per-channel ID chain | ID chain + AEAD | Switches from K to ID |
| ID Generation | Server | Server | Server | **Client** | Server | **Either party** |
| Pre-shared Secret | Required | Required | Required (low entropy) | Required | Required | **Not required** |
| Authentication | Yes | Yes | Yes | Yes | Yes | **No** |
| Server Key Table | Required | Required | Required | Required | Required | **Not required** |
| Forward Secrecy | No | Yes | Yes | Yes | Yes | Yes |
| Asynchronous Concurrency | No | No | No | **Yes** | No | No |
| Authenticated Encryption | No | No | No | No | **Yes (mandatory)** | No |
| Dual Replay Protection | No | No | No | Yes (seq + window) | **Yes (ID + seq)** | No |
| Offline Brute Force Risk | Yes | Yes | **None** | Yes | Yes | **None** |
| Implementation Examples | Yes | Yes | **No** | Yes (spec) | Yes (spec) | Yes (spec) |
| Best For | Max compatibility | Forward secrecy | Low-entropy pre-shared secrets | High concurrency | **CBC replacement, high assurance** | **Anonymous, zero-config** |

---

## Reading Recommendations

- **For understanding the fundamental protocol**: Start with CHAP documentation
- **For learning the underlying theory**: Read CHAP first, then the ZIM concept
- **For forward secrecy with authentication**: Review CHAP-IEM after understanding standard CHAP
- **For low-entropy pre-shared secrets** (PIN codes, etc.): Study CHAP-IEM-SKN specification (theoretical, use with caution)
- **For high-concurrency scenarios** (HTTP/2, real-time games, parallel APIs): Read CHAP-IEM-CSW specification
- **For CBC replacement and higher cryptographic assurance**: Read CHAP-IEM-AEAD specification
- **For anonymous, zero-configuration scenarios** (IoT, temporary access): Read CHAP-DH specification
- **For bidirectional communication patterns**: Read BCPF documentation
- **For implementation decisions**: Compare the trade-offs across all variants in the Quick Comparison table
