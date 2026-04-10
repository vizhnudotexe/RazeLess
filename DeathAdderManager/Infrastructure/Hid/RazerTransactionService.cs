using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

public sealed class RazerTransactionService
{
    private readonly IHidTransport _transport;
    private readonly ILogger<RazerTransactionService> _logger;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 20;

    public RazerTransactionService(IHidTransport transport, ILogger<RazerTransactionService> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    public async Task<bool> SendCommandAsync(byte[] packet, CancellationToken ct = default)
    {
        if (packet.Length < 3)
        {
            _logger.LogError("Packet too short - missing Transaction ID");
            return false;
        }

        byte transactionId = packet[2];
        _logger.LogDebug("Sending command: Class=0x{PClass:X2} Cmd=0x{PCmd:X2} TransID=0x{TransID:X2}",
            packet[7], packet[8], transactionId);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var sendOk = await _transport.SendFeatureReportAsync(packet, ct);
            if (!sendOk)
            {
                _logger.LogWarning("Send attempt {Attempt} failed", attempt);
                if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
                continue;
            }

            // The mouse needs time to process the request
            await Task.Delay(10, ct);

            bool processing = true;
            int pollCount = 0;
            while(processing && pollCount < 50)
            {
                pollCount++;
                var response = await _transport.ReadFeatureReportAsync(ct);
                if (response == null)
                {
                    await Task.Delay(5, ct);
                    continue;
                }

                byte status = RazerHidPacket.GetStatus(response);
                
                // Status byte meanings:
                // 0x00: New/Processing
                // 0x01: Busy
                // 0x02: OK
                // 0x03: Not Supported / Error
                // 0x04: Timeout
                if (status == 0x00 || status == 0x01)
                {
                    await Task.Delay(10, ct); // Wait and poll again without resending
                    continue;
                }

                _logger.LogDebug("Response: Status=0x{Status:X2} TransID=0x{TransID:X2} CRC_OK={CrcOk}",
                    status, RazerHidPacket.GetTransactionId(response),
                    RazerHidPacket.ValidateCrc(response));

                processing = false;

                if (status == RazerHidPacket.StatusError || status == 0x04 || status == 0x05)
                {
                    _logger.LogWarning("Device returned error status 0x{S:X2} on attempt {A}", status, attempt);
                    if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
                    break;
                }

                if (status == RazerHidPacket.StatusOk)
                {
                    if (RazerHidPacket.GetTransactionId(response) != transactionId)
                    {
                        _logger.LogWarning("TransID mismatch on attempt {Attempt} (Expected: {Exp}, Got: {Got})", attempt, transactionId, RazerHidPacket.GetTransactionId(response));
                        // Some mice return 0x00 for trans ID on apply packets, we shouldn't necessarily fail.
                    }
                    if (!RazerHidPacket.ValidateCrc(response))
                    {
                        _logger.LogWarning("CRC validation failed on attempt {Attempt}, but status is OK. Accepting.", attempt);
                    }

                    _logger.LogInformation("Command 0x{TransID:X2} succeeded (Status OK)", transactionId);
                    return true;
                }
                
                _logger.LogWarning("Unhandled status 0x{Status:X2}, attempt {Attempt}", status, attempt);
                break;
            }
        }

        _logger.LogError("Command 0x{TransID:X2} failed after {MaxRetries} retries", transactionId, MaxRetries);
        return false;
    }

    public async Task<byte[]?> SendCommandWithResponseAsync(byte[] packet, CancellationToken ct = default)
    {
        if (packet.Length < 3)
        {
            _logger.LogError("Packet too short - missing Transaction ID");
            return null;
        }

        byte transactionId = packet[2];

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var sendOk = await _transport.SendFeatureReportAsync(packet, ct);
            if (!sendOk)
            {
                if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
                continue;
            }

            bool processing = true;
            int pollCount = 0;
            while (processing && pollCount < 50)
            {
                pollCount++;
                var response = await _transport.ReadFeatureReportAsync(ct);
                if (response == null)
                {
                    await Task.Delay(5, ct);
                    continue;
                }

                byte status = RazerHidPacket.GetStatus(response);
                if (status == 0x00 || status == 0x01)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                processing = false;
                var transId = RazerHidPacket.GetTransactionId(response);
                var crcOk = RazerHidPacket.ValidateCrc(response);
                _logger.LogDebug("Response(with data): Status=0x{Status:X2} TransID=0x{TransID:X2} CRC_OK={CrcOk}",
                    status, transId, crcOk);

                if (status == RazerHidPacket.StatusOk)
                {
                    if (transId != transactionId)
                        _logger.LogWarning("TransID mismatch on response cmd (Expected: 0x{Exp:X2}, Got: 0x{Got:X2})", transactionId, transId);
                    if (!crcOk)
                        _logger.LogWarning("CRC mismatch on response cmd 0x{TransID:X2}, accepting due to status OK", transactionId);
                    return response;
                }

                if (status == RazerHidPacket.StatusError || status == 0x04 || status == 0x05)
                    break;
            }

            if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
        }

        return null;
    }
}
