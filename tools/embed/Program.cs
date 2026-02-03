using System.CommandLine;
using System.Text.Json;
using EmbedCLI.Services;

namespace EmbedCLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Embedding CLI for Flow RAG system");
        rootCommand.Name = "flow";
        
        // test-download 명령어 (모델 다운로드 테스트)
        var testDownloadCommand = new Command("test-download", "Test model download functionality");
        testDownloadCommand.SetHandler(async () =>
        {
            try
            {
                var manager = new ModelManager();
                var paths = await manager.EnsureModelFilesAsync();
                
                Console.WriteLine("✅ Model files ready:");
                Console.WriteLine($"  - Model: {paths.ModelPath}");
                Console.WriteLine($"  - Tokenizer: {paths.TokenizerPath}");
                Console.WriteLine($"  - TokenizerConfig: {paths.TokenizerConfigPath}");
                Console.WriteLine($"  - Config: {paths.ConfigPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });
        rootCommand.AddCommand(testDownloadCommand);

        // test-tokenizer 명령어 (토크나이저 테스트)
        var testTokenizerCommand = new Command("test-tokenizer", "Test tokenizer functionality");
        var textArgument = new Argument<string>("text", () => "Hello World", "Text to tokenize");
        testTokenizerCommand.AddArgument(textArgument);
        testTokenizerCommand.SetHandler(async (string text) =>
        {
            try
            {
                var manager = new ModelManager();
                var paths = await manager.EnsureModelFilesAsync();
                
                var tokenizer = new TokenizerService(paths.TokenizerPath);
                var output = tokenizer.Tokenize(text);
                tokenizer.PrintDebugInfo(output);
                
                Console.WriteLine("✅ Tokenizer test passed");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, textArgument);
        rootCommand.AddCommand(testTokenizerCommand);

        // test-inference 명령어 (추론 테스트)
        var testInferenceCommand = new Command("test-inference", "Test inference functionality");
        testInferenceCommand.SetHandler(async () =>
        {
            try
            {
                var manager = new ModelManager();
                var paths = await manager.EnsureModelFilesAsync();
                
                var tokenizer = new TokenizerService(paths.TokenizerPath);
                using var inference = new InferenceService(paths.ModelPath);
                
                var testText = "Hello World";
                var tokens = tokenizer.Tokenize(testText);
                
                Console.WriteLine($"Input: {testText}");
                
                var inferenceInput = new InferenceInput
                {
                    InputIds = tokens.InputIds,
                    AttentionMask = tokens.AttentionMask
                };
                
                var output = inference.RunInference(inferenceInput);
                
                Console.WriteLine($"✅ Inference completed");
                Console.WriteLine($"   Sequence Length: {output.SequenceLength}");
                Console.WriteLine($"   Hidden Size: {output.HiddenSize}");
                Console.WriteLine($"   First hidden state sample: [{string.Join(", ", output.HiddenStates[0].Take(5).Select(x => $"{x:F4}"))}...]");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });
        rootCommand.AddCommand(testInferenceCommand);

        // test-pipeline 명령어 (전체 파이프라인 테스트)
        var testPipelineCommand = new Command("test-pipeline", "Test full embedding pipeline");
        var pipelineTextArgument = new Argument<string>("text", () => "Hello World", "Text to embed");
        testPipelineCommand.AddArgument(pipelineTextArgument);
        testPipelineCommand.SetHandler(async (string text) =>
        {
            try
            {
                var manager = new ModelManager();
                var paths = await manager.EnsureModelFilesAsync();
                
                var tokenizer = new TokenizerService(paths.TokenizerPath);
                using var inference = new InferenceService(paths.ModelPath);
                var postProcessor = new PostProcessingService();
                
                Console.WriteLine($"Input: {text}");
                
                // 1. 토큰화
                var tokens = tokenizer.Tokenize(text);
                Console.WriteLine($"[INFO] Tokenized: {tokens.InputIds.Length} tokens");
                
                // 2. 추론
                var inferenceInput = new InferenceInput
                {
                    InputIds = tokens.InputIds,
                    AttentionMask = tokens.AttentionMask
                };
                var inferenceOutput = inference.RunInference(inferenceInput);
                Console.WriteLine($"[INFO] Inference completed: {inferenceOutput.HiddenSize} dimensions");
                
                // 3. 후처리
                var embedding = postProcessor.Process(new PostProcessingInput
                {
                    HiddenStates = inferenceOutput.HiddenStates,
                    AttentionMask = tokens.AttentionMask
                });
                
                // 4. 검증
                postProcessor.ValidateEmbedding(embedding);
                
                // 5. 출력
                Console.WriteLine($"✅ Embedding generated");
                Console.WriteLine($"   Dimension: {embedding.Dimension}");
                Console.WriteLine($"   First 10 values: [{string.Join(", ", embedding.Vector.Take(10).Select(x => $"{x:F4}"))}]");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, pipelineTextArgument);
        rootCommand.AddCommand(testPipelineCommand);

        // benchmark 명령어 (성능 벤치마크)
        var benchmarkCommand = new Command("benchmark", "Run performance benchmark");
        var benchmarkIterationsOption = new Option<int>("--iterations", () => 10, "Number of iterations");
        benchmarkCommand.AddOption(benchmarkIterationsOption);
        benchmarkCommand.SetHandler(async (int iterations) =>
        {
            try
            {
                var manager = new ModelManager();
                var paths = await manager.EnsureModelFilesAsync();
                
                var tokenizer = new TokenizerService(paths.TokenizerPath);
                var testText = "Benchmark test text for performance measurement.";
                var tokens = tokenizer.Tokenize(testText);

                Console.WriteLine("=== Performance Benchmark ===");
                Console.WriteLine($"Iterations: {iterations}");
                Console.WriteLine();

                // Warmup
                using var inference = new InferenceService(paths.ModelPath);
                var postProcessor = new PostProcessingService();
                
                _ = inference.RunInference(new InferenceInput
                {
                    InputIds = tokens.InputIds,
                    AttentionMask = tokens.AttentionMask
                });

                // 반복 측정
                var times = new List<double>();
                for (int i = 0; i < iterations; i++)
                {
                    var start = DateTime.UtcNow;
                    var inferenceOutput = inference.RunInference(new InferenceInput
                    {
                        InputIds = tokens.InputIds,
                        AttentionMask = tokens.AttentionMask
                    });
                    _ = postProcessor.Process(new PostProcessingInput
                    {
                        HiddenStates = inferenceOutput.HiddenStates,
                        AttentionMask = tokens.AttentionMask
                    });
                    var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
                    times.Add(elapsed);
                }

                Console.WriteLine($"Average inference time: {times.Average():F2}ms");
                Console.WriteLine($"Min: {times.Min():F2}ms, Max: {times.Max():F2}ms");
                Console.WriteLine("✅ Benchmark completed");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, benchmarkIterationsOption);
        rootCommand.AddCommand(benchmarkCommand);
        
        // embed-file 명령어 (파일 임베딩)
        var embedFileCommand = new Command("embed-file", "Generate embedding from file");
        var fileArgument = new Argument<string>("file", "Path to text file");
        embedFileCommand.AddArgument(fileArgument);
        embedFileCommand.SetHandler(async (string filePath) =>
        {
            Environment.ExitCode = await HandleEmbedFileAsync(filePath);
        }, fileArgument);
        rootCommand.AddCommand(embedFileCommand);

        // cache-stats 명령어 (캐시 통계)
        var cacheStatsCommand = new Command("cache-stats", "Show cache statistics");
        cacheStatsCommand.SetHandler(() =>
        {
            var cache = new CacheService();
            cache.PrintStats();
        });
        rootCommand.AddCommand(cacheStatsCommand);

        // cache-clear 명령어 (캐시 삭제)
        var cacheClearCommand = new Command("cache-clear", "Clear embedding cache");
        cacheClearCommand.SetHandler(() =>
        {
            var cache = new CacheService();
            cache.Clear();
        });
        rootCommand.AddCommand(cacheClearCommand);
        
        // 루트 명령어: 기본 임베딩 처리 (embed 명령어와 호환)
        var rootTextArgument = new Argument<string?>("text", () => null, "Text to embed");
        rootCommand.AddArgument(rootTextArgument);
        rootCommand.SetHandler(async (string? text) =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.Error.WriteLine("ERROR: Text is required.");
                Environment.ExitCode = 2;
                return;
            }

            Environment.ExitCode = await HandleEmbedAsync(text);
        }, rootTextArgument);

        var processedArgs = args;
        if (processedArgs.Length > 0 && string.Equals(processedArgs[0], "embed", StringComparison.OrdinalIgnoreCase))
        {
            processedArgs = processedArgs.Skip(1).ToArray();
        }

        return await rootCommand.InvokeAsync(processedArgs);
    }

    static async Task<int> HandleEmbedAsync(string text)
    {
        try
        {
            // 캐시 체크
            var cache = new CacheService();
            var cached = cache.Get(text);
            
            if (cached.Hit && cached.Embedding != null)
            {
                var json = JsonSerializer.Serialize(cached.Embedding, EmbedJsonContext.Default.SingleArray);
                Console.WriteLine(json);
                return 0;
            }

            // 서비스 초기화
            var manager = new ModelManager();
            var paths = await manager.EnsureModelFilesAsync();

            var tokenizer = new TokenizerService(paths.TokenizerPath);
            using var inference = new InferenceService(paths.ModelPath);
            var postProcessor = new PostProcessingService();

            // 파이프라인 실행
            var tokens = tokenizer.Tokenize(text);
            var inferenceOutput = inference.RunInference(new InferenceInput
            {
                InputIds = tokens.InputIds,
                AttentionMask = tokens.AttentionMask
            });

            var embedding = postProcessor.Process(new PostProcessingInput
            {
                HiddenStates = inferenceOutput.HiddenStates,
                AttentionMask = tokens.AttentionMask
            });

            // 캐시 저장
            cache.Set(text, embedding.Vector);

            // JSON 출력 (stdout) - AOT 호환
            var json2 = JsonSerializer.Serialize(embedding.Vector, EmbedJsonContext.Default.SingleArray);
            Console.WriteLine(json2);

            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR: Invalid input: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Inference failed: {ex.Message}");
            return 3;
        }
    }

    static async Task<int> HandleEmbedFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"ERROR: File not found: {filePath}");
                return 4;
            }

            var text = await File.ReadAllTextAsync(filePath);
            return await HandleEmbedAsync(text);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ERROR: File read failed: {ex.Message}");
            return 4;
        }
    }
}
