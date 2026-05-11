# CHAP‑IEM‑AEAD Technical Document

## Adding Authenticated Encryption and Explicit Nonce Support to CHAP‑IEM

> **NOTE:** This protocol is NOT the legacy Challenge‑Handshake Authentication Protocol (CHAP). It is a security‑hardened variant of the Chain Hash Authentication Protocol – ID Encryption Mode (CHAP‑IEM).

---

## I. Overview

CHAP‑IEM‑AEAD is a security‑hardened variant of the original CHAP‑IEM that mandates **authenticated encryption (AEAD)** and introduces **explicit nonce / sequence number management**.

**Problems Solved:**

| Problem | Original CHAP‑IEM | CHAP‑IEM‑AEAD |
|---------|-------------------|----------------|
| Encryption mode | Unspecified (CBC padding oracle risk) | Mandatory AEAD |
| Integrity protection | Indirect via ID unpredictability | Direct via authenticated encryption |
| Nonce management | None (implementer responsibility) | Protocol‑mandated explicit nonce |
| Replay protection | ID single‑use only | ID single‑use + sequence number monotonic check |

**Solution Approach:**

- Mandate **AES‑256‑GCM** or **ChaCha20‑Poly1305** authenticated encryption
- Each packet carries an explicit 12‑byte nonce embedding a monotonic sequence number
- Guarantee that `(key, nonce)` pairs are never repeated
- Receiver checks sequence number monotonicity, forming **dual replay protection** together with ID single‑use
- Preserve all CHAP‑IEM benefits: forward secrecy, automatic recovery, ID as key

---

## II. Comparison with Original CHAP‑IEM

| Feature | Original CHAP‑IEM | CHAP‑IEM‑AEAD |
|---------|-------------------|----------------|
| Encryption mode | Unspecified (CBC padding risk) | Mandatory AEAD (GCM or Poly1305) |
| Integrity protection | Indirect via ID unpredictability | Direct via authenticated encryption |
| Nonce management | None (user responsibility) | Protocol: 12‑byte explicit nonce with sequence number |
| Replay protection | ID single‑use only | ID single‑use + sequence number monotonic check |
| Per‑packet overhead | 0 bytes | 12 bytes nonce + 16 bytes tag |
| Forward secrecy | ✅ | ✅ |
| Automatic recovery | ✅ | ✅ |
| MitM protection | ✅ (via pre‑shared K) | ✅ (via pre‑shared K) |

---

## III. Core Protocol Design

### 3.1 Encryption Algorithm

- **AEAD algorithms**: AES‑256‑GCM (recommended) or ChaCha20‑Poly1305
- **Key length**: 256 bits (ID itself is the key; must have 256 bits of entropy)
- **Nonce length**: 12 bytes (96 bits), explicitly transmitted by the protocol

### 3.2 Nonce Construction Rules

Each session (or each channel, reserving for future CSW‑style concurrency) independently maintains two **64‑bit send counters**:

- `client_send_seq`: counter for requests sent by the client
- `server_send_seq`: counter for responses sent by the server

**Nonce format (12 bytes):**

```
nonce = [ channel_id (4 bytes) ][ sequence_number (8 bytes) ]
```

- `channel_id`: current channel identifier (always 0 for single‑channel mode)
- `sequence_number`: send counter value (big‑endian)

After each packet is sent, the corresponding direction counter increments by 1. Since each key `ID_n` is used for exactly one request and one response, and the counter increases monotonically, the `(key, nonce)` pair is globally unique — fully satisfying AEAD security requirements.

### 3.3 Packet Format

#### Login Phase (same as CHAP‑IEM, but using AEAD)

**Client → Server:**

```
[ 12‑byte nonce ][ AES256‑GCM_K( username ) ][ 16‑byte tag ]
```

**Server → Client (on success):**

```
[ 12‑byte nonce' ][ AES256‑GCM_K( "OK" || ID₁ ) ][ 16‑byte tag ]
```

#### Operation Phase (key switched to IDₙ)

**Request Packet (Client → Server):**

```
[ 12‑byte nonce ][ ciphertext ][ 16‑byte tag ]
```

Where:
- `ciphertext = AEAD_encrypt( key = current_ID, nonce, plaintext )`
- `plaintext = command` (original IEM does not carry next_id in the request)

**Response Packet (Server → Client):**

```
[ 12‑byte nonce' ][ ciphertext' ][ 16‑byte tag' ]
```

Where:
- `plaintext' = result || ID_{n+1}`

The client decrypts the response using the same `current_ID` (i.e., IDₙ used for the request), obtains `ID_{n+1}`, and updates its key.

> **Note:** Unlike CSW, this follows the original IEM pattern where the server allocates the next ID. Requests do not contain a client‑pre‑generated next_id.

### 3.4 Sequence Number Checking Rules

Both parties maintain receive counters:

| Maintainer | Variable | Initial Value |
|------------|----------|---------------|
| Client | `last_recv_server_seq` | -1 |
| Server | `last_recv_client_seq` | -1 |

Upon receiving a packet, extract the `sequence_number` from the nonce:

- Client receiving a response: MUST have `seq > last_recv_server_seq`, otherwise drop
- Server receiving a request: MUST have `seq > last_recv_client_seq`, otherwise drop

After successful check, update the corresponding `last_recv_*_seq`.

**Purpose and significance:** Even if an attacker replays an old ciphertext (and the ID happens to remain valid due to some exceptional window), the sequence number check will still reject it, forming a second line of defense.

### 3.5 Compatibility with Original IEM Automatic Recovery

The exception recovery mechanism remains unchanged. When the server detects an invalid ID, it encrypts a recovery packet using the pre‑shared key K (or K_session in SKN):

```
Recovery Packet = [ nonce ][ AES256‑GCM_K( "resync" || current_valid_ID ) ][ tag ]
```

The recovery packet also follows the AEAD format, carrying a nonce (using an independent counter under K). The client decrypts and updates its key.

---

## IV. Security Analysis

### 4.1 Integrity Protection

If the original IEM uses only CBC mode, an attacker could tamper with ciphertext and exploit decryption results or padding error oracles to extract information. AEAD mode ensures that any tampering with ciphertext causes decryption to fail (tag mismatch), resulting in immediate discard.

### 4.2 Replay Protection

| Layer | Mechanism | Description |
|-------|-----------|-------------|
| First | ID single‑use | Server destroys old ID; replayed requests are rejected due to invalid ID |
| Second | Sequence number monotonic check | Even if ID state persists unexpectedly, sequence number exposes replay |

**Dual protection**, exceeding the single‑layer sequence number mechanism of original IEM as well as TLS/SSH.

### 4.3 Nonce Reuse Prevention

The protocol mandates a monotonically increasing 64‑bit sequence number as part of the nonce, and each `ID_n` is used for exactly one request and one response. Thus, every `(key, nonce)` pair is globally unique, completely eliminating the catastrophic risk of GCM nonce reuse.

### 4.4 Forward Secrecy

Same as original IEM: after each request/response, the key updates to a fresh random `IDₙ₊₁`, and the old ID is immediately destroyed. Obtaining the current key does not allow derivation of past keys.

### 4.5 Man‑in‑the‑Middle Attacks

CHAP‑IEM‑AEAD, like the original CHAP‑IEM, **does protect against MitM attacks** because the pre‑shared key `K` (or `K_session` in SKN mode) is **never transmitted** over the network.

**Why MitM is not possible without knowing K:**

- An attacker cannot decrypt the login handshake (encrypted with `K`)
- An attacker cannot forge a valid login packet (requires `K` to encrypt)
- An attacker cannot establish independent sessions with the client and server simultaneously

**The only prerequisite for a successful MitM attack is that the attacker already knows `K`.** If `K` is compromised, the attacker can impersonate either party — but this is a **key compromise** issue, not a protocol vulnerability. Key secrecy is the user's responsibility, just as with any other symmetric‑key protocol (SSH, TLS‑PSK, WireGuard).

### 4.6 Security Summary

| Threat | CHAP‑IEM‑AEAD Protection |
|--------|--------------------------|
| Eavesdropping | ✅ (AES‑256 encryption) |
| Ciphertext tampering | ✅ (AEAD tag verification) |
| Replay attack | ✅ (ID single‑use + sequence number) |
| Nonce reuse | ✅ (protocol‑enforced monotonic counters) |
| Padding oracle | ✅ (AEAD eliminates CBC padding) |
| Man‑in‑the‑middle | ✅ (requires knowledge of pre‑shared K) |
| Forward secrecy | ✅ (ID chain destroys old keys) |

---

## V. Implementation Guidelines

### 5.1 Algorithm Selection

| Environment | Recommendation | Alternative |
|-------------|----------------|-------------|
| General (hardware AES acceleration) | AES‑256‑GCM | AES‑256‑GCMSIV (if nonce misuse tolerance needed) |
| Low‑end embedded / software implementation | ChaCha20‑Poly1305 | — |

### 5.2 Counter Initialization

Send counters for client and server may start at 0, or may be initialized with a random value (adding some unpredictability). The receiver only needs to check monotonicity (greater than the last received), not continuity.

### 5.3 Error Handling

| Error Type | Handling |
|------------|----------|
| Decryption failure (tag mismatch) | Discard packet; optionally log |
| Sequence number too small or duplicate | Discard packet; do NOT trigger recovery logic |
| ID invalid (but decryption succeeds) | Follow original IEM recovery procedure |

### 5.4 Memory and Bandwidth Overhead

- Per‑packet overhead: **28 bytes** (12 nonce + 16 tag)
- For most command‑response interactions (tens to hundreds of bytes per message), overhead is acceptable
- If bandwidth is extremely constrained, consider ChaCha20‑Poly1305 (tag still 16 bytes) or reducing nonce length (e.g., 8 bytes) — not recommended

### 5.5 Compatibility with CSW or DH

This design can be used independently, or embedded as a foundational component into CSW (multi‑channel) or DH (anonymous) variants, by maintaining independent sequence number spaces per channel.

---

## VI. Applicable Scenarios

**CHAP‑IEM‑AEAD is particularly suitable for:**

- Existing CHAP‑IEM systems concerned about CBC security, wishing to upgrade to AEAD
- High‑sensitivity environments requiring dual‑layer replay protection
- Engineering teams needing explicit nonce management specifications to avoid implementation errors
- Scenarios that can accept 28 bytes of per‑packet overhead but cannot tolerate the full complexity of TLS
- Deployments that already rely on CHAP‑IEM's MitM protection (via pre‑shared `K`) and want stronger cryptographic guarantees

**Not suitable for:**

- Extremely bandwidth‑constrained environments (e.g., tens of thousands of packets per second with tiny payloads)
- Scenarios where pre‑shared key distribution is operationally impossible (consider CHAP‑DH for anonymous use cases)

---

## VII. Summary

CHAP‑IEM‑AEAD builds upon original CHAP‑IEM by mandating authenticated encryption and explicit sequence number management:

| Improvement | Effect |
|-------------|--------|
| Mandatory AEAD | Eliminates CBC padding oracle risk |
| Explicit nonce + monotonic sequence number | Prevents catastrophic GCM nonce reuse |
| Dual replay protection | ID single‑use + sequence number checking |
| Clear MitM protection statement | Documents that pre‑shared `K` provides MitM resistance |

It preserves all IEM advantages (forward secrecy, automatic recovery, lightweight session state, MitM protection via pre‑shared key) while aligning with modern cryptographic best practices.

This variant can be considered a **security‑hardened profile** of IEM, suitable for deployments requiring higher reliability and willing to accept moderate overhead.

---

## VIII. Protocol Hierarchy

```
ZIM (Zigzag Interaction Model)
  ├── CHAP (fixed key + ID chain + authentication)
  ├── CHAP-IEM (ID as key + authentication)
  ├── CHAP-IEM-SKN (pre‑shared Y + DH mixing + authentication)
  ├── CHAP-IEM-CSW (concurrent sliding window extension)
  ├── CHAP-IEM-AEAD (authenticated encryption + explicit nonce)  ← this document
  └── CHAP-DH (anonymous + standard DH)
```

---

## IX. References

- CHAP‑IEM.md – original ID Encryption Mode specification
- NIST SP 800‑38D – AES‑GCM specification
- RFC 8439 – ChaCha20‑Poly1305

*Document version: 1.1* (MitM section corrected)
