using Tokenizers.DotNet;

namespace EmbedCLI.Services;

/// <summary>
/// XLM-Roberta 기반 토크나이저
/// </summary>
public class TokenizerService
{
    private readonly Tokenizers.DotNet.Tokenizer _tokenizer;
    private const int MaxLength = 512;

    public TokenizerService(string tokenizerPath)
    {
        if (!File.Exists(tokenizerPath))
        {
            throw new FileNotFoundException($"Tokenizer not found: {tokenizerPath}");
        }

        // tokenizer.json 로드 (HuggingFace Tokenizers)
        _tokenizer = new Tokenizers.DotNet.Tokenizer(tokenizerPath);
    }

    public TokenizerOutput Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be empty", nameof(text));
        }

        // 기본 인코딩
        var encoding = _tokenizer.Encode(text);
        var inputIds = encoding.Select(id => (int)id).ToList();

        // 길이 제한
        if (inputIds.Count > MaxLength)
        {
            inputIds = inputIds.Take(MaxLength).ToList();
        }

        // AttentionMask (모두 1)
        var attentionMask = Enumerable.Repeat(1, inputIds.Count).ToArray();

        return new TokenizerOutput
        {
            InputIds = inputIds.ToArray(),
            AttentionMask = attentionMask,
            SequenceLength = inputIds.Count
        };
    }

    public TokenizerOutput TokenizeWithPadding(string text, int targetLength = MaxLength)
    {
        var output = Tokenize(text);

        if (output.SequenceLength >= targetLength)
        {
            return output;
        }

        // 패딩 추가 (PAD token = 1)
        var paddedInputIds = output.InputIds.Concat(
            Enumerable.Repeat(1, targetLength - output.SequenceLength)
        ).ToArray();

        var paddedAttentionMask = output.AttentionMask.Concat(
            Enumerable.Repeat(0, targetLength - output.SequenceLength)
        ).ToArray();

        return new TokenizerOutput
        {
            InputIds = paddedInputIds,
            AttentionMask = paddedAttentionMask,
            SequenceLength = output.SequenceLength  // 실제 길이 유지
        };
    }

    public void PrintDebugInfo(TokenizerOutput output)
    {
        Console.Error.WriteLine($"[DEBUG] Tokens: {output.SequenceLength}");
        Console.Error.WriteLine($"[DEBUG] InputIds: [{string.Join(", ", output.InputIds.Take(10))}...]");
        Console.Error.WriteLine($"[DEBUG] AttentionMask: [{string.Join(", ", output.AttentionMask.Take(10))}...]");
    }
}

public class TokenizerOutput
{
    public required int[] InputIds { get; init; }
    public required int[] AttentionMask { get; init; }
    public required int SequenceLength { get; init; }
}
