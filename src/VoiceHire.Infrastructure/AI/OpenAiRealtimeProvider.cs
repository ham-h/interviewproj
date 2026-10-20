using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceHire.Application.Abstractions;

namespace VoiceHire.Infrastructure.AI;

/// <summary>
/// OpenAI Realtime over WebRTC using the server-forwarded SDP flow: the browser posts its SDP offer
/// to our API, we post it to /realtime/calls with the server-side key and the session config, and
/// return the SDP answer. The call id from the Location header lets the call monitor attach.
/// </summary>
public class OpenAiRealtimeProvider(HttpClient http, IOptions<OpenAiOptions> options) : IRealtimeInterviewProvider
{
    public async Task<RealtimeCallResult> CreateCallAsync(RealtimeCallRequest request, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var session = new
        {
            type = "realtime",
            model = o.RealtimeModel,
            instructions = request.Instructions,
            output_modalities = new[] { "audio" },
            audio = new
            {
                input = new
                {
                    transcription = new { model = o.TranscriptionModel, language = o.TranscriptionLanguage },
                    noise_reduction = new { type = "near_field" },
                    // Low eagerness gives candidates time to think before the turn is considered over.
                    turn_detection = new { type = "semantic_vad", eagerness = "low", create_response = true, interrupt_response = true }
                },
                output = new { voice = o.Voice }
            },
            tools = InterviewTools.Definitions,
            tool_choice = "auto"
        };

        using var form = new MultipartFormDataContent
        {
            { new StringContent(request.SdpOffer, Encoding.UTF8), "sdp" },
            { new StringContent(JsonSerializer.Serialize(session), Encoding.UTF8, "application/json"), "session" }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "realtime/calls") { Content = form };
        message.Headers.Add("OpenAI-Safety-Identifier", request.SafetyIdentifier);

        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new RealtimeProviderException($"Realtime call creation failed ({(int)response.StatusCode}): {body}");

        // Location: /v1/realtime/calls/rtc_xxx
        var callId = response.Headers.Location?.OriginalString.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? throw new RealtimeProviderException("Realtime call created without a call id in the Location header.");

        return new RealtimeCallResult(body, callId);
    }

    public static void Configure(HttpClient client, OpenAiOptions o)
    {
        client.BaseAddress = new Uri(o.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
    }
}

public class RealtimeProviderException(string message) : Exception(message);
