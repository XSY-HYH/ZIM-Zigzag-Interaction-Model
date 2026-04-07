using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace ZIM.Server
{
    public class ChapIemSession
    {
        public string SessionId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public DateTime LastActivity { get; set; }
    }

    public class ChapIemRequest
    {
        public string? SessionId { get; set; }
        public string? Action { get; set; }
        public JsonElement? Data { get; set; }
    }

    public class ChapIemResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? NewId { get; set; }
        public object? Data { get; set; }
    }

    public class ChapIemHandler
    {
        private readonly byte[] _masterKey;
        private readonly ConcurrentDictionary<string, ChapIemSession> _sessions = new();
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public ChapIemHandler(string adminPassword)
        {
            using var sha256 = SHA256.Create();
            _masterKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(adminPassword));
        }

        private byte[] Encrypt(byte[] key, string plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
            return result;
        }

        private string? Decrypt(byte[] key, byte[] ciphertextWithIv)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var iv = new byte[16];
                Buffer.BlockCopy(ciphertextWithIv, 0, iv, 0, 16);
                aes.IV = iv;

                var ciphertext = new byte[ciphertextWithIv.Length - 16];
                Buffer.BlockCopy(ciphertextWithIv, 16, ciphertext, 0, ciphertext.Length);

                using var decryptor = aes.CreateDecryptor();
                var plainBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null;
            }
        }

        private string GenerateSessionId()
        {
            var bytes = new byte[32];
            _rng.GetBytes(bytes);
            return Convert.ToHexString(bytes);
        }

        private byte[] Ensure256BitKey(string id)
        {
            var idBytes = Encoding.UTF8.GetBytes(id);
            if (idBytes.Length == 32)
                return idBytes;
            
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(idBytes);
        }

        private ChapIemResponse ErrorResponse(string message, string? newId = null)
        {
            return new ChapIemResponse { Success = false, Message = message, NewId = newId };
        }

        private ChapIemResponse SuccessResponse(string message, string? newId = null, object? data = null)
        {
            return new ChapIemResponse { Success = true, Message = message, NewId = newId, Data = data };
        }

        public byte[] HandleLogin(byte[] encryptedData, string clientId)
        {
            var decrypted = Decrypt(_masterKey, encryptedData);
            if (decrypted == null)
            {
                var response = ErrorResponse("authentication_failed");
                return Encrypt(_masterKey, JsonSerializer.Serialize(response));
            }

            var username = decrypted.Trim();
            if (username != "admin")
            {
                var response = ErrorResponse("authentication_failed");
                return Encrypt(_masterKey, JsonSerializer.Serialize(response));
            }

            var sessionId = GenerateSessionId();
            _sessions[sessionId] = new ChapIemSession
            {
                SessionId = sessionId,
                ClientId = clientId,
                LastActivity = DateTime.UtcNow
            };

            var successResponse = SuccessResponse("login_success", sessionId);
            return Encrypt(_masterKey, JsonSerializer.Serialize(successResponse));
        }

        public byte[] HandleOperation(byte[] encryptedData, string clientId)
        {
            string? sessionId = null;
            byte[]? currentKey = null;
            ChapIemSession? session = null;

            try
            {
                foreach (var s in _sessions.Values)
                {
                    if (s.ClientId == clientId)
                    {
                        var testKey = Ensure256BitKey(s.SessionId);
                        var testDecrypt = Decrypt(testKey, encryptedData);
                        if (testDecrypt != null)
                        {
                            sessionId = s.SessionId;
                            currentKey = testKey;
                            session = s;
                            break;
                        }
                    }
                }

                if (sessionId == null || currentKey == null || session == null)
                {
                    var response = ErrorResponse("out_of_sync");
                    return Encrypt(_masterKey, JsonSerializer.Serialize(response));
                }

                var decrypted = Decrypt(currentKey, encryptedData);
                if (decrypted == null)
                {
                    var response = ErrorResponse("decryption_failed");
                    return Encrypt(_masterKey, JsonSerializer.Serialize(response));
                }

                ChapIemRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<ChapIemRequest>(decrypted);
                }
                catch
                {
                    var response = ErrorResponse("invalid_request");
                    return Encrypt(_masterKey, JsonSerializer.Serialize(response));
                }

                if (request?.SessionId != sessionId)
                {
                    var response = ErrorResponse("session_mismatch");
                    return Encrypt(_masterKey, JsonSerializer.Serialize(response));
                }

                _sessions.TryRemove(sessionId, out _);

                var newSessionId = GenerateSessionId();
                _sessions[newSessionId] = new ChapIemSession
                {
                    SessionId = newSessionId,
                    ClientId = clientId,
                    LastActivity = DateTime.UtcNow
                };

                var newKey = Ensure256BitKey(newSessionId);
                object? operationResult = null;
                if (request.Action != null)
                {
                    operationResult = new { action = request.Action, processed = true, timestamp = DateTime.UtcNow };
                }

                var response = SuccessResponse("operation_success", newSessionId, operationResult);
                return Encrypt(currentKey, JsonSerializer.Serialize(response));
            }
            catch (Exception)
            {
                var response = ErrorResponse("internal_error");
                return Encrypt(_masterKey, JsonSerializer.Serialize(response));
            }
        }

        public bool ValidateSession(string sessionId, string clientId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return false;
            
            if (session.ClientId != clientId)
                return false;
            
            if (DateTime.UtcNow - session.LastActivity > TimeSpan.FromHours(1))
            {
                _sessions.TryRemove(sessionId, out _);
                return false;
            }
            
            session.LastActivity = DateTime.UtcNow;
            return true;
        }

        public void CleanupExpiredSessions()
        {
            var expired = _sessions
                .Where(s => DateTime.UtcNow - s.Value.LastActivity > TimeSpan.FromHours(1))
                .Select(s => s.Key)
                .ToList();
            
            foreach (var id in expired)
                _sessions.TryRemove(id, out _);
        }
    }
}
