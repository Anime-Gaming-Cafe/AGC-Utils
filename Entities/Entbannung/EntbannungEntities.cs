namespace AGC_Management.Entities.Entbannung;

public sealed record Antragssperre(ulong UserId, string Reason, long ExpiresAt);

public sealed record AntragsverlaufEntry(
    ulong UserId,
    string AntragsId,
    bool Entbannt,
    string Reason,
    ulong ModId,
    long Timestamp);

public sealed record Abstimmung(
    ulong ChannelId,
    ulong MessageId,
    long ExpiresAt,
    ulong CreatedBy,
    int PositiveVotes,
    int NegativeVotes,
    bool EndPending);

public enum VoteType
{
    Positive,
    Negative
}
