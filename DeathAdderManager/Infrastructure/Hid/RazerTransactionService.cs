using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

/// <summary>
/// Wraps every HID send cycle with retry + ACK logic.
///
/// Two send paths:
///   SendCommandAsync          — full retry + ACK check.
///                               Used for DPI, polling rate, device mode.
///
///   SendFireAndForgetAsync    — single send, no ACK wait, no retry.
///                               Used for ALL Class=0x03 (LED) commands on PID 0x0098.
///
/// Why fire-and-forget for LED?
///   The DeathAdder Essential 2021 (PID 0x0098) has only ONE HID interface
///   with feature-report capability (mi_00, MaxFeatureReportLength=91).
///   All other interfaces have MaxFeatureReportLength=0 and cannot receive
///   91-byte feature reports. The mi_00 interface returns Status=0x05
///   (Not Supported) for every Class=0x03 LED command, while accepting
///   Class=0x04 DPI and Class=0x00 polling commands correctly.
///   Retrying LED commands wastes ~150ms per profile apply and fills the
///   log with false errors. Fire-and-forget avoids both problems.
///   If a future firmware update enables LED support, this code will silently
///   start working without needing changes.
/// </summary>
public sealed class RazerTransactionService
{
    private readonly IHidTransport                    _transport;
    private readonly ILogger<RazerTransactionService> _logger;

    private const int MaxRetries   = 3;
    private const int RetryDelayMs = 20;
    private const int SettleMs     = 10;   // wait after send before reading ACK
    private const int MaxPollCycles = 50;

    public RazerTransactionService(IHidTransport transport, ILogger<RazerTransactionService> logger)
    {
        _transport = transport;
        _logger    = logger;
    }

    // ── Fire-and-forget  (all Class=0x03 LED commands) ───────────────────────

    /// <summary>
    /// Send one packet and return immediately — no ACK read, no retry.
    /// Correct for LED commands on PID 0x0098 which either silently accept
    /// the packet or return 0x05 (Not Supported) on the mi_00 interface.
    /// Either way there is nothing useful to do with the response.
    /// </summary>
    public async Task<bool> SendFireAndForgetAsync(byte[] packet, CancellationToken ct = default)
    {
        var ok = await _transport.SendFeatureReportAsync(packet, ct);
        // Brief settle so firmware can process before the next command arrives
        await Task.Delay(SettleMs, ct);
        _logger.LogDebug("LED FAF: Class=0x{C:X2} Cmd=0x{Id:X2} sent={Ok}",
            packet[7], packet[8], ok);
        return ok;
    }

    // ── Send-and-ACK  (DPI, polling rate, device mode, apply commit) ─────────

    /// <summary>
    /// Send packet and poll for Status=0x02 (OK) from the device.
    /// Retries up to <see cref="MaxRetries"/> times with a short back-off.
    /// </summary>
    public async Task<bool> SendCommandAsync(byte[] packet, CancellationToken ct = default)
    {
        if (packet.Length < 9)
        {
            _logger.LogError("Packet too short ({Len} bytes)", packet.Length);
            return false;
        }

        byte transactionId = packet[2];
        byte cmdClass      = packet[7];
        byte cmdId         = packet[8];

        _logger.LogDebug(
            "Sending command: Class=0x{C:X2} Cmd=0x{Id:X2} TransID=0x{Tx:X2} DataSize=0x{Sz:X2} PacketHex={Hex}",
            cmdClass, cmdId, transactionId, packet[6],
            BitConverter.ToString(packet, 0, Math.Min(packet.Length, 20)).Replace("-", " "));

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var sendOk = await _transport.SendFeatureReportAsync(packet, ct);
            if (!sendOk)
            {
                _logger.LogWarning("Send failed attempt {A}/{Max}", attempt, MaxRetries);
                if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
                continue;
            }

            await Task.Delay(SettleMs, ct);

            for (int poll = 0; poll < MaxPollCycles; poll++)
            {
                var response = await _transport.ReadFeatureReportAsync(ct);
                if (response == null) { await Task.Delay(5, ct); continue; }

                byte status = RazerHidPacket.GetStatus(response);

                if (status == RazerHidPacket.StatusNew || status == RazerHidPacket.StatusBusy)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                _logger.LogDebug("Response: Status=0x{S:X2} TransID=0x{T:X2} CRC_OK={Crc}",
                    status, RazerHidPacket.GetTransactionId(response), RazerHidPacket.ValidateCrc(response));

                if (status == RazerHidPacket.StatusOk)
                {
                    if (RazerHidPacket.GetTransactionId(response) != transactionId)
                        _logger.LogDebug("TxID mismatch (expected {E}, got {G}) — still OK",
                            transactionId, RazerHidPacket.GetTransactionId(response));

                    if (!RazerHidPacket.ValidateCrc(response))
                        _logger.LogDebug("CRC mismatch on response, status=OK, accepting");

                    _logger.LogInformation("Command 0x{Tx:X2} succeeded (Status OK)", transactionId);
                    return true;
                }

                // 0x03=failure, 0x04=timeout, 0x05=not supported
                _logger.LogWarning("Device returned error status 0x{S:X2} on attempt {A}",
                    status, attempt);
                break;
            }

            if (attempt < MaxRetries) await Task.Delay(RetryDelayMs, ct);
        }

        _logger.LogError("Command 0x{Tx:X2} failed after {Max} retries", transactionId, MaxRetries);
        return false;
    }

    /// <summary>
    /// Send a read-back command and return the full response packet.
    /// Used for DPI readback (GetDpi, 0x85) diagnostics.
    /// </summary>
    public async Task<byte[]?> SendCommandWithResponseAsync(byte[] packet, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            var sendOk = await _transport.SendFeatureReportAsync(packet, ct);
            if (!sendOk) { await Task.Delay(RetryDelayMs, ct); continue; }

            await Task.Delay(SettleMs, ct);

            var response = await _transport.ReadFeatureReportAsync(ct);
            if (response == null) { await Task.Delay(RetryDelayMs, ct); continue; }

            byte status = RazerHidPacket.GetStatus(response);
            if (status == RazerHidPacket.StatusBusy)
            {
                await Task.Delay(RetryDelayMs, ct); continue;
            }
            if (status == RazerHidPacket.StatusOk && RazerHidPacket.ValidateCrc(response))
                return response;
        }
        return null;
    }
}
