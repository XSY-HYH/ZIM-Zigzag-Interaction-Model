# CHAP‑IEM‑CSW Technical Document

## Concurrent Sliding Window Extension for CHAP‑IEM

> **NOTE:** This protocol is NOT the legacy Challenge‑Handshake Authentication Protocol (CHAP). This is an extension of the Chain Hash Authentication Protocol – ID Encryption Mode (CHAP‑IEM).

---

## I. Overview

CHAP‑IEM‑CSW (Concurrent Sliding Window) is an extension of **CHAP‑IEM** that enables **asynchronous concurrent requests** while fully preserving forward secrecy and replay protection.

- **Core problem solved:** Original CHAP‑IEM forces strict sequential order (request N must wait for response N before sending request N+1). This blocks HTTP/2 multiplexing, parallel API calls, and any high‑concurrency scenario.
- **Solution:** Introduce **multiple independent logical channels** per session, each with its own ID chain and a **sliding window** for request numbering. Clients can **pre‑generate next IDs** and send requests on any channel without waiting for previous responses.
- **Security preserved:** All benefits of CHAP‑IEM remain – forward secrecy, replay attack resistance, and automatic recovery (via the original K / K_session channel).

---

## II. Comparison with Original CHAP‑IEM

| Feature                      | CHAP‑IEM (original)     | CHAP‑IEM‑CSW                     |
|------------------------------|-------------------------|----------------------------------|
| Asynchronous concurrency     | ❌ Not supported         | ✅ Yes (multi‑channel + windows) |
| Request ordering per channel | Strict sequential       | Sequential within a channel, but channels run in parallel |
| ID generation                | Server‑assigned         | Client‑generated (pre‑forwarded) |
| Replay protection            | Single ID invalidation  | Sequence numbers + ID sliding window |
| Forward secrecy              | ✅ Yes                  | ✅ Yes                           |
| Exception recovery           | Via K / K_session       | Same + per‑channel reset         |
| Ideal for                   | Low‑concurrency, order‑critical ops | High‑concurrency, modern web/API |

---

## III. Core Concepts

### 3.1 Multiple Channels (C‑channels)

- A session consists of **C independent logical channels** (e.g., C = 32).
- Each channel `ch` maintains its own **ID chain**:  
  `ID_ch0` (initial) → `ID_ch1` → `ID_ch2` → ...
- The number `C` is negotiated during login (fixed for the session).
- Channels are independent: a lost packet on channel 3 does not block channel 7.

### 3.2 Client‑Generated Next IDs

- In original CHAP‑IEM, the server returns `ID_n+1` inside the response.
- In CHAP‑IEM‑CSW, the **client generates** the next ID (`next_id`) locally (must be CSPRNG, 256 bits) and sends it **inside the request**.
- After sending the request, the client **immediately updates** its local key for that channel to `next_id`. No waiting for server acknowledgment (optimistic update).
- The server verifies `next_id` and, if valid, updates its own state for the channel accordingly.

### 3.3 Sequence Numbers and Sliding Window

Each channel has:

- `seq_num` (32‑bit unsigned integer, starting at 0) – request counter.
- Sender (client) increments `seq_num` for each new request on that channel.
- Receiver (server) maintains:
  - `expected_seq[ch]` – the next expected sequence number.
  - A **sliding window** of recently used `(seq_num, next_id)` pairs (size W, e.g., 64).
- Requests on the same channel **must be sent in increasing seq_num order** (no skipping).  
  *This allows the server to detect replayed or out‑of‑order packets.*

### 3.4 Packets Format

**Login Phase** (identical to CHAP‑IEM, but with multiple initial IDs)

Client → Server: `AES256_K(username)`

Server → Client (if success):  
`AES256_K( "OK" || ID_0_0 || ID_1_0 || ... || ID_{C-1}_0 )`

Where each `ID_ch_0` is a fresh 256‑bit random value.

**Operation Request** (client → server)

```text
Request = {
    ch : uint16,           // channel index 0..C-1
    seq : uint32,          // sequence number for this channel
    next_id : bytes(32),   // the new ID that will become the key for next request on this channel
    encrypted_payload : bytes   // actual command (AES256 encrypted with current ID of this channel)
}
```

Encryption: use `AES256_{ current_ID_of_ch }` over the **whole** (ch || seq || next_id || payload).

**Operation Response** (server → client)

```text
Response = {
    ch : uint16,
    seq : uint32,          // same seq as the request it responds to
    status : uint16,       // success/error code
    result : bytes         // encrypted with the *same* ID that decrypted the request
}
```

Important: The server **does not** return a new ID – the client already knows the next ID (`next_id`).

---

## IV. Protocol Walkthrough

### 4.1 Login and Channel Setup

- Client and server pre‑share key K (or complete SKN exchange).
- Client sends `AES256_K(username)`.
- Server verifies, generates C random 256‑bit IDs: `ID_0_0, ID_1_0, ..., ID_{C-1}_0`.
- Server replies `AES256_K( OK || ID_0_0 || ID_1_0 || ... || ID_{C-1}_0 )`.
- Client decrypts and stores `current_ID[ch] = ID_ch_0` for all ch.
- Both sides initialise `expected_seq[ch] = 0` and empty sliding windows.

### 4.2 Normal Concurrent Request

Suppose client wants to send two independent commands on channels 0 and 1 **simultaneously**.

**Channel 0 request:**

- Client picks current `ID = current_ID[0]` ( = `ID_0_0`).
- Generates new random `next_id0`.
- Encrypts `(ch=0, seq=0, next_id0, cmd_A)` with `ID_0_0`.
- Sends packet.
- Immediately updates `current_ID[0] = next_id0`, and increments its local `seq` counter for channel 0 to 1.

**Channel 1 request (sent immediately, no wait):**

- Uses `current_ID[1] = ID_1_0`, generates `next_id1`, encrypts `(ch=1, seq=0, next_id1, cmd_B)`.
- Sends, updates `current_ID[1] = next_id1`, local seq becomes 1.

**Server processes (in any order):**

- For channel 0 request: decrypts with `ID_0_0`, checks `seq == expected_seq[0] (0)`, stores `(seq=0, next_id0)` in sliding window, executes `cmd_A`, updates `expected_seq[0]=1`, and `current_ID[0] = next_id0`.
- For channel 1 request: same independent process.
- Responses sent back, each encrypted with the **same ID that decrypted the request** (i.e., `ID_0_0` for channel 0 response, `ID_1_0` for channel 1).

### 4.3 Replay Attack Mitigation

Attacker captures packet `(ch=0, seq=0, next_id0, cmd_A)` encrypted with `ID_0_0` and replays it later.

- Server now has `expected_seq[0] = 1` (or larger).
- Received `seq=0` is less than expected → **rejected** (out‑of‑order/old).  
Even if `expected_seq` somehow matched (which never happens after one valid request), the sliding window would remember that `(seq=0, next_id0)` was already used and reject the duplicate.

### 4.4 Out‑of‑Order Arrival (Same Channel)

Because of network delays, request `seq=1` might arrive before `seq=0` on the same channel.

- Server’s `expected_seq[ch]` is still 0.
- Packet with `seq=1` is received → `seq` > expected → server **buffers** it (or rejects if buffering not implemented).  
  *Recommendation:* Server can keep a small out‑of‑order buffer (size equal to window) and process in order after missing seq arrives.  
  Alternatively, client must ensure strict in‑order transmission per channel (e.g., by using a single sending queue).

### 4.5 Sliding Window Maintenance

The server maintains a fixed‑size window (e.g., 64 entries) of recently seen `(seq, next_id)` pairs per channel.

- When a new valid request `(seq, next_id)` is processed:
  - Add `(seq, next_id)` to the window.
  - Remove entries older than `seq - W` (where W is window size).
- For replay detection, before accepting `(seq, next_id)`:
  - If `seq` is already in window → reject.
  - If `seq` < `expected_seq[ch]` but not found in window → also reject (it’s too old and already evicted, considered replay).

### 4.6 Exception Recovery

Two types of failures:

**1. Response lost** (client sent request, server processed it, but response never arrived)

- Client times out for that specific request.
- Because the client already advanced `current_ID[ch]` to `next_id`, it **cannot retransmit the same request** (the old key is gone).
- **Solution:** Client opens a **new channel** (if any left) or uses the original recovery mechanism via K / K_session to reset the whole session (or reset the affected channel).  
  *Simpler approach:* Client treats a lost response as a channel failure and moves the conversation to a fresh channel, leaving the stale channel for garbage collection.

**2. Server out‑of‑sync** (client lost a response and tries to send next request using an ID that server no longer accepts)

- Server receives a request with `current_ID` that does NOT match its stored `current_ID[ch]` for that channel.
- Server falls back to the original CHAP‑IEM recovery: sends `AES256_K("resync" || current_valid_ID || ...)` on the recovery channel.
- Client recovers using K (or K_session) as in standard CHAP‑IEM.

---

## V. Security Analysis

### 5.1 Replay Attacks

- Attackers cannot replay old requests because:
  - Sequence numbers must strictly increase.
  - Each `(seq, next_id)` pair is stored in a sliding window; duplicates are rejected.
  - Even if sequence number space wraps (2³² operations), the window ensures that very old packets (beyond window size) are rejected because their `seq` < `expected_seq - W` and not present in window.

### 5.2 Forward Secrecy

- Each request uses a fresh encryption key (`current_ID`).
- Old keys are destroyed immediately after use (server) or after sending (client optimistic update).
- Compromising a later key does not allow decryption of previous traffic because previous keys cannot be derived.

### 5.3 Client‑Generated ID Security

- IDs must be generated using a CSPRNG (256 bits).
- Predictable IDs would allow an attacker to guess future keys.
- The server *must* verify that `next_id` is fresh (not previously used) and random‑looking (no explicit check needed beyond uniqueness).

### 5.4 Channel Independence

- Compromising one channel does not affect others (different ID chains).
- However, the recovery key K (or K_session) is shared across all channels – if K leaks, the attacker can reset any channel. This is acceptable because K is the root secret.

### 5.5 Limit on Number of Channels (C)

- C should be a fixed, moderate number (e.g., 32, 64, 128).
- Too many channels increase server state and potential for abuse (channel exhaustion).
- Servers SHOULD implement idle channel reclamation.

---

## VI. Implementation Guidelines

### 6.1 Random Number Generation

- All IDs (`ID_ch0`, `next_id`) and any random values **MUST** be generated using a cryptographically secure pseudorandom number generator (CSPRNG).
- Do NOT use sequential, timestamp‑based, or low‑entropy values.

### 6.2 Encryption Algorithms

- Same as CHAP‑IEM:
  - AES‑256 in GCM mode (recommended) or CBC with HMAC‑SHA‑256.
  - If only CBC is available, rely on the ID chain for integrity and use identical error responses to avoid padding oracles.

### 6.3 Sliding Window Size (W)

- Recommend W = 64.
- Larger windows tolerate more out‑of‑order packets but increase memory.
- W must be at least the maximum expected out‑of‑order depth.

### 6.4 Channel Count (C)

- Suggest C = 32 as default.
- Can be negotiated during login (client sends `C` in login request, server caps at max configured).

### 6.5 Timeouts and Retransmission

- Clients should implement per‑request timeouts.
- If a response does not arrive, client SHOULD NOT retransmit the same request on the same channel (because the key has already advanced). Instead:
  - Use a different channel (if available) to send the same logical command.
  - Or trigger full session recovery via K.

### 6.6 Memory Management

- Servers MUST bound the size of per‑channel sliding windows and out‑of‑order buffers to avoid memory exhaustion attacks.
- Idle channels should be reset after a configurable timeout (e.g., 5 minutes).

---

## VII. Applicable Scenarios

CHAP‑IEM‑CSW is especially suitable for:

- **HTTP/2, HTTP/3, gRPC:** Multiple streams within a single encrypted session.
- **Real‑time multiplayer games:** Many small independent actions (movement, chat, inventory) sent concurrently.
- **IoT command pipelines:** Sensor readings and control commands arriving asynchronously.
- **Web frontend with parallel API calls:** A single session handling many simultaneous fetch/XHR requests.

Scenarios **not** recommended:

- Very low‑power embedded devices where only one request at a time is ever needed (use original CHAP‑IEM for simplicity).
- When network order is strictly required and concurrency offers no benefit.

---

## VIII. Summary

CHAP‑IEM‑CSW extends the original CHAP‑IEM protocol to support **asynchronous concurrent requests** without sacrificing any security property. By introducing:

- **Multiple independent channels**,
- **Client‑generated rolling IDs**,
- **Sequence numbers with sliding window replay protection**,

it lifts the strict sequential bottleneck and enables modern, high‑performance communication patterns while retaining forward secrecy, lightweight implementation, and automatic recovery.

The protocol remains fully compatible with the Zigzag Interaction Model (ZIM) – each request still advances the state; only now multiple state chains can advance in parallel.

---

## IX. References

- CHAP‑IEM.md – original ID Encryption Mode specification
- pih.md – performance optimisation guide
- BCPF.md – bidirectional communication patterns
- ZIM (Zigzag Interaction Model) – theoretical foundation

*Document version: 1.0*
