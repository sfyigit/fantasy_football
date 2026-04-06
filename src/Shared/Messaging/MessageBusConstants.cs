using Shared.Domain.Events;

namespace Shared.Messaging;

/// <summary>
/// Single source of truth for RabbitMQ exchange, queue, and routing key names.
/// All publishers and consumers must reference these constants.
/// </summary>
public static class MessageBusConstants
{
    /// <summary>Topic exchange used by all services.</summary>
    public const string Exchange = "fantasy.events";

    public static class RoutingKeys
    {
        public const string MatchUpdated      = nameof(MatchUpdatedEvent);
        public const string PlayerStatUpdated = nameof(PlayerStatUpdatedEvent);
        public const string ScoreCalculated   = nameof(ScoreCalculatedEvent);
    }

    public static class Queues
    {
        /// <summary>ScoringWorker listens on this queue for PlayerStatUpdated events.</summary>
        public const string ScoringWorker     = "scoring-worker.player-stat-updated";

        /// <summary>LeaderboardWorker listens on this queue for ScoreCalculated events.</summary>
        public const string LeaderboardWorker = "leaderboard-worker.score-calculated";
    }
}
