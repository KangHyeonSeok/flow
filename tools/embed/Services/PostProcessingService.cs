// Services/PostProcessingService.cs
namespace EmbedCLI.Services;

public class PostProcessingService
{
    public EmbeddingVector Process(PostProcessingInput input)
    {
        // 1. Mean Pooling (AttentionMask 고려)
        var pooled = MeanPooling(input.HiddenStates, input.AttentionMask);
        
        // 2. L2 Normalization
        var normalized = L2Normalize(pooled);
        
        return new EmbeddingVector
        {
            Vector = normalized,
            Dimension = normalized.Length
        };
    }

    private float[] MeanPooling(float[][] hiddenStates, int[] attentionMask)
    {
        var seqLen = hiddenStates.Length;
        var hiddenSize = hiddenStates[0].Length;
        
        var pooled = new float[hiddenSize];
        var validTokenCount = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 1)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    pooled[j] += hiddenStates[i][j];
                }
                validTokenCount++;
            }
        }

        // 평균 계산
        if (validTokenCount > 0)
        {
            for (int i = 0; i < hiddenSize; i++)
            {
                pooled[i] /= validTokenCount;
            }
        }

        return pooled;
    }

    private float[] L2Normalize(float[] vector)
    {
        // L2 Norm 계산
        var sumOfSquares = 0.0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }
        var norm = Math.Sqrt(sumOfSquares);

        if (norm < 1e-12)  // 거의 0인 벡터 방지
        {
            return vector;
        }

        // 정규화
        var result = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            result[i] = (float)(vector[i] / norm);
        }
        return result;
    }

    public void ValidateEmbedding(EmbeddingVector embedding)
    {
        var sumOfSquares = 0.0;
        for (int i = 0; i < embedding.Vector.Length; i++)
        {
            sumOfSquares += embedding.Vector[i] * embedding.Vector[i];
        }
        var norm = Math.Sqrt(sumOfSquares);
        
        Console.Error.WriteLine($"[DEBUG] Vector dimension: {embedding.Dimension}");
        Console.Error.WriteLine($"[DEBUG] L2 Norm: {norm:F6}");
        
        if (Math.Abs(norm - 1.0) > 0.01)
        {
            Console.Error.WriteLine($"[WARNING] Vector not properly normalized: {norm}");
        }
    }
}

public class PostProcessingInput
{
    public required float[][] HiddenStates { get; init; }
    public required int[] AttentionMask { get; init; }
}

public class EmbeddingVector
{
    public required float[] Vector { get; init; }
    public required int Dimension { get; init; }
}
