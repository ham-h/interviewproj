namespace VoiceHire.Application.Abstractions;

/// <summary>
/// A speech-to-speech provider. The browser's WebRTC offer is forwarded through our server,
/// so the provider credential never reaches the client and the server learns the call id it
/// needs to observe transcripts and answer tool calls.
/// </summary>
public interface IRealtimeInterviewProvider
{
    Task<RealtimeCallResult> CreateCallAsync(RealtimeCallRequest request, CancellationToken cancellationToken);
}

public sealed record RealtimeCallRequest(
    Guid SessionId,
    string SdpOffer,
    string Instructions,
    string SafetyIdentifier);

public sealed record RealtimeCallResult(string SdpAnswer, string ProviderCallId);

/// <summary>Watches a live call server-side: persists transcripts, answers tool calls, enforces time limits.</summary>
public interface IRealtimeCallMonitor
{
    void Attach(Guid sessionId, string providerCallId);
    void Detach(Guid sessionId);
}
