using Microsoft.AI.DevTeam.Skills;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Orleans.Runtime;

namespace Microsoft.AI.DevTeam;

public class Dev : SemanticPersona, IDevelopCode
{
    private readonly IKernel _kernel;
    private readonly ISemanticTextMemory _memory;
    private readonly ILogger<Dev> _logger;

    protected override string MemorySegment => "dev-memory";

    public Dev([PersistentState("state", "messages")] IPersistentState<SemanticPersonaState> state, IKernel kernel, ISemanticTextMemory memory, ILogger<Dev> logger) : base(state)
    {
        _kernel = kernel;
        _memory = memory;
        _logger = logger;
    }

    public async Task<string> GenerateCode(string ask)
    {
        try
        {
            var function = _kernel.CreateSemanticFunction(Developer.Implement, new OpenAIRequestSettings { MaxTokens = 15000, Temperature = 0.8, TopP = 1 });
            var context = new ContextVariables();
            if (_state.State.History == null) _state.State.History = new List<ChatHistoryItem>();
            _state.State.History.Add(new ChatHistoryItem
            {
                Message = ask,
                Order = _state.State.History.Count + 1,
                UserType = ChatUserType.User
            });
            await AddWafContext(_memory, ask, context);
            context.Set("input", ask);

            var result = await _kernel.RunAsync(context, function);
            var resultMessage = result.ToString();
            _state.State.History.Add(new ChatHistoryItem
            {
                Message = resultMessage,
                Order = _state.State.History.Count + 1,
                UserType = ChatUserType.Agent
            });
            await _state.WriteStateAsync();

            return resultMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating code");
            return default;
        }
    }



    public Task<string> ReviewPlan(string plan)
    {
        throw new NotImplementedException();
    }

    public async Task<UnderstandingResult> BuildUnderstanding(string content)
    {
        try
        {
            var explainFunction = _kernel.CreateSemanticFunction(Developer.Explain, new OpenAIRequestSettings { MaxTokens = 15000, Temperature = 0.8, TopP = 1 });
            var consolidateFunction = _kernel.CreateSemanticFunction(Developer.ConsolidateUnderstanding, new OpenAIRequestSettings { MaxTokens = 15000, Temperature = 0.8, TopP = 1 });
            var explainContext = new ContextVariables();
            explainContext.Set("input", content);
            var explainResult = await _kernel.RunAsync(explainContext, explainFunction);
            var explainMesage = explainResult.ToString();

            var consolidateContext = new ContextVariables();
            consolidateContext.Set("input", _state.State.Understanding);
            consolidateContext.Set("newUnderstanding", explainMesage);

            var consolidateResult = await _kernel.RunAsync(consolidateContext, consolidateFunction);
            var consolidateMessage = consolidateResult.ToString();

            _state.State.Understanding = consolidateMessage;
            await _state.WriteStateAsync();

            return new UnderstandingResult
            {
                NewUnderstanding = consolidateMessage,
                Explanation = explainMesage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building understanding");
            return default;
        }
    }
}