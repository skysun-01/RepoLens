using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Questions;

namespace RepoLens.Api.Streaming;

/// <summary>
/// Maps answer events to Server-Sent Events: <c>start</c>, many <c>delta</c>, then <c>done</c>.
/// A failure mid-stream (the HTTP status is already sent) becomes a final <c>error</c> event.
/// </summary>
internal static partial class AnswerServerSentEvents
{
    public static async IAsyncEnumerable<SseItem<object>> From(
        IAsyncEnumerable<AnswerStreamEvent> events,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            SseItem<object> item;
            var isLast = false;

            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                item = Map(enumerator.Current);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStreamFailed(logger, ex);
                var (code, message) = ex is AppException app
                    ? (app.Code, app.Message)
                    : ("answer_failed", "The answer could not be completed. Try again.");
                item = new SseItem<object>(new { code, message }, "error");
                isLast = true;
            }

            yield return item;

            if (isLast)
            {
                break;
            }
        }
    }

    private static SseItem<object> Map(AnswerStreamEvent streamEvent) => streamEvent switch
    {
        AnswerStarted started => new SseItem<object>(new { started.ConversationId, started.CommitSha }, started.EventType),
        AnswerDelta delta => new SseItem<object>(new { delta.Text }, delta.EventType),
        AnswerCompleted completed => new SseItem<object>(completed.Answer, completed.EventType),
        _ => throw new InvalidOperationException($"Unknown answer event {streamEvent.GetType().Name}."),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Streaming an answer failed")]
    private static partial void LogStreamFailed(ILogger logger, Exception exception);
}
