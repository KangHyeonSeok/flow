using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EmbedCLI.Services;

/// <summary>
/// ONNX Runtime 추론 서비스
/// </summary>
public class InferenceService : IDisposable
{
    private InferenceSession? _session;
    private readonly string _modelPath;
    private DateTime _lastUsed = DateTime.MinValue;
    private Timer? _unloadTimer;
    private const int UnloadTimeoutSeconds = 600; // 10분

    public InferenceService(string modelPath)
    {
        _modelPath = modelPath;
        
        // 10분 후 자동 언로드
        _unloadTimer = new Timer(_ => CheckAndUnload(), null, 
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    private void CheckAndUnload()
    {
        if (_session != null && 
            (DateTime.UtcNow - _lastUsed).TotalSeconds > UnloadTimeoutSeconds)
        {
            Console.Error.WriteLine("[INFO] Unloading model due to inactivity");
            _session?.Dispose();
            _session = null;
            GC.Collect();
        }
    }

    private SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions();

        // GPU 시도 (DirectML)
        try
        {
            options.AppendExecutionProvider_DML(0);  // Device 0
            Console.Error.WriteLine("[INFO] DirectML GPU acceleration enabled");
            return options;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARNING] DirectML not available: {ex.Message}");
            Console.Error.WriteLine("[INFO] Falling back to CPU");
        }

        // CPU 폴백
        return options;
    }

    private InferenceSession GetOrCreateSession()
    {
        if (_session == null)
        {
            Console.Error.WriteLine("[INFO] Loading ONNX model...");
            var options = CreateSessionOptions();
            
            try
            {
                _session = new InferenceSession(_modelPath, options);
                Console.Error.WriteLine("[INFO] Model loaded successfully");
            }
            catch (OutOfMemoryException)
            {
                Console.Error.WriteLine("[ERROR] GPU out of memory, retrying with CPU");
                options = new SessionOptions();  // CPU only
                _session = new InferenceSession(_modelPath, options);
                Console.Error.WriteLine("[INFO] Model loaded with CPU fallback");
            }
        }

        _lastUsed = DateTime.UtcNow;
        return _session;
    }

    public InferenceOutput RunInference(InferenceInput input)
    {
        var session = GetOrCreateSession();

        // 입력 텐서 생성
        var inputIdsShape = new[] { 1, input.InputIds.Length };
        var inputIdsTensor = new DenseTensor<long>(
            input.InputIds.Select(x => (long)x).ToArray(),
            inputIdsShape
        );

        var attentionMaskShape = new[] { 1, input.AttentionMask.Length };
        var attentionMaskTensor = new DenseTensor<long>(
            input.AttentionMask.Select(x => (long)x).ToArray(),
            attentionMaskShape
        );

        // 입력 딕셔너리
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        // 추론 실행
        var startTime = DateTime.UtcNow;
        using var results = session.Run(inputs);
        var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

        Console.Error.WriteLine($"[DEBUG] Inference time: {elapsedMs:F2}ms");

        // Hidden States 추출 (last_hidden_state 출력 사용)
        var outputResult = results.First();
        var outputTensor = outputResult.AsEnumerable<float>().ToArray();
        var shape = outputResult.AsTensor<float>().Dimensions.ToArray();
        
        // [batch, seq_len, hidden_size] -> [seq_len, hidden_size]
        var seqLen = shape[1];
        var hiddenSize = shape[2];
        
        var hiddenStates = new float[seqLen][];
        for (int i = 0; i < seqLen; i++)
        {
            hiddenStates[i] = new float[hiddenSize];
            Array.Copy(outputTensor, i * hiddenSize, hiddenStates[i], 0, hiddenSize);
        }

        return new InferenceOutput
        {
            HiddenStates = hiddenStates,
            SequenceLength = seqLen,
            HiddenSize = hiddenSize
        };
    }

    public void Dispose()
    {
        _unloadTimer?.Dispose();
        _session?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class InferenceInput
{
    public required int[] InputIds { get; init; }
    public required int[] AttentionMask { get; init; }
}

public class InferenceOutput
{
    public required float[][] HiddenStates { get; init; }
    public required int SequenceLength { get; init; }
    public required int HiddenSize { get; init; }
}
