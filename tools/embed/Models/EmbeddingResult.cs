namespace EmbedCLI.Models;

/// <summary>
/// 임베딩 결과를 담는 클래스
/// </summary>
public class EmbeddingResult
{
    /// <summary>
    /// 정규화된 임베딩 벡터 (1024 차원)
    /// </summary>
    public required float[] Vector { get; init; }
    
    /// <summary>
    /// 벡터 차원 수
    /// </summary>
    public int Dimension => Vector.Length;
    
    /// <summary>
    /// 추론에 걸린 시간 (밀리초)
    /// </summary>
    public double InferenceTimeMs { get; init; }
    
    /// <summary>
    /// 캐시 히트 여부
    /// </summary>
    public bool FromCache { get; init; }
}
