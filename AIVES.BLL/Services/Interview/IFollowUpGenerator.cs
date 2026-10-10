using AIVES.DTO;

namespace AIVES.BLL.Services.Interview;

/// <summary>Decides whether an answer needs a probing follow-up and, if so, writes it.</summary>
public interface IFollowUpGenerator
{
    bool IsConfigured
    {
        get;
    }

    /// <summary>Throws when the model cannot be reached or returns something unusable.</summary>
    Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default);
}
