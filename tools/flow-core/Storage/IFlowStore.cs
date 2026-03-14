namespace FlowCore.Storage;

/// <summary>통합 접근점. DI 편의용.</summary>
public interface IFlowStore : ISpecStore, IAssignmentStore, IReviewRequestStore, IActivityStore { }
