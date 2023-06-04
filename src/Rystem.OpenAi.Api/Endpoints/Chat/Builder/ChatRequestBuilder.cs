﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Rystem.OpenAi.Chat
{
    public sealed class ChatRequestBuilder : RequestBuilder<ChatRequest>
    {
        private ChatModelType _modelType;
        internal ChatRequestBuilder(HttpClient client, OpenAiConfiguration configuration,
            ChatMessage message, IOpenAiUtility utility) : base(client,
            configuration,
            () =>
            {
                return new ChatRequest()
                {
                    Messages = new List<ChatMessage>() { message },
                    ModelId = ChatModelType.Gpt35Turbo0301.ToModel().Id
                };
            }, utility)
        {
            _familyType = ModelFamilyType.Gpt3_5;
            _modelType = ChatModelType.Gpt35Turbo0301;
        }
        /// <summary>
        /// Execute operation.
        /// </summary>
        /// <returns>ChatResult</returns>
        public ValueTask<ChatResult> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            Request.Stream = false;
            return Client.PostAsync<ChatResult>(Configuration.GetUri(OpenAiType.Chat, Request.ModelId!, _forced, string.Empty), Request, Configuration, cancellationToken);
        }
        /// <summary>
        /// Execute operation.
        /// </summary>
        /// <returns>CostResult<ChatResult></returns>
        public async ValueTask<CostResult<ChatResult>> ExecuteAndCalculateCostAsync(CancellationToken cancellationToken = default)
        {
            var response = await ExecuteAsync(cancellationToken);
            return new CostResult<ChatResult>(response, () => CalculateCost(OpenAiType.Chat, response?.Usage));
        }
        /// <summary>
        /// Specifies where the results should stream and be returned at one time.
        /// </summary>
        /// <returns>ChatResult</returns>
        public async IAsyncEnumerable<StreamingChatResult> ExecuteAsStreamAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Request.Stream = true;
            var results = new StreamingChatResult
            {
                Composed = new ChatResult
                {
                    Choices = new List<ChatChoice>(),
                    Usage = new CompletionUsage()
                    {
                        CompletionTokens = 0,
                        TotalTokens = 0,
                        PromptTokens = 0
                    }
                },
                Chunks = new List<ChatResult>()
            };
            var chatRole = ChatRole.Assistant;
            var index = -1;
            await foreach (var result in Client.StreamAsync<ChatResult>(Configuration.GetUri(OpenAiType.Chat, Request.ModelId!, _forced, string.Empty), Request, HttpMethod.Post, Configuration, null, cancellationToken))
            {
                if (result?.Choices != null && result.Choices.Count > 0 && result.Choices[0].Delta != null)
                {
                    var currentIndex = result.Choices[0].Index;
                    if (index != currentIndex)
                    {
                        chatRole = result.Choices[0].Delta!.Role;
                        index = currentIndex;
                        BuildLastMessage();
                        results.Composed.Choices.Add(result.Choices[0]);
                    }
                    else
                        result.Choices[0].Delta!.Role = chatRole;
                    results.Chunks.Add(result);
                    results.Composed.Choices.Last().Message ??= new ChatMessage { Role = chatRole, Content = string.Empty };
                    if (result.Choices[0].Delta?.Content != null)
                    {
                        var lastMessage = results!.Composed!.Choices!.Last().Message!;
                        lastMessage
                            .AddContent(result.Choices[0].Delta!.Content);
                        results.Composed.Usage.TotalTokens += 1;
                        results.Composed.Usage.CompletionTokens += 1;
                    }
                    yield return results;
                }
            }
            BuildLastMessage();

            void BuildLastMessage()
            {
                var lastMessage = results?.Composed?.Choices?.LastOrDefault()?.Message;
                lastMessage?.BuildContent();
            }
        }
        /// <summary>
        /// Specifies where the results should stream and be returned at one time.
        /// </summary>
        /// <returns>CostResult<ChatResult></returns>
        public async IAsyncEnumerable<CostResult<StreamingChatResult>> ExecuteAsStreamAndCalculateCostAsync(
           [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var response in ExecuteAsStreamAsync(cancellationToken))
            {
                yield return new CostResult<StreamingChatResult>(response, () => CalculateCost(OpenAiType.Chat, response?.Composed?.Usage));
            }
        }
        /// <summary>
        /// Add a message to the request
        /// </summary>
        /// <param name="message">Prompt</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddMessage(ChatMessage message)
        {
            Request.Messages ??= new List<ChatMessage>();
            Request.Messages.Add(message);
            return this;
        }
        /// <summary>
        /// Add a message to the request
        /// </summary>
        /// <param name="content"></param>
        /// <param name="role"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddMessage(string content, ChatRole role = ChatRole.User)
            => AddMessage(new ChatMessage { Content = content, Role = role });
        /// <summary>
        /// User message is a message used to send information.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddUserMessage(string content)
            => AddMessage(new ChatMessage { Content = content, Role = ChatRole.User });
        /// <summary>
        /// System message is a message used to improve the response from chat api.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddSystemMessage(string content)
            => AddMessage(new ChatMessage { Content = content, Role = ChatRole.System });
        /// <summary>
        /// Assistant message is the response from chat api, usually you don't need to set this message.
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddAssistantMessage(string content)
            => AddMessage(new ChatMessage { Content = content, Role = ChatRole.Assistant });
        /// <summary>
        /// ID of the model to use.
        /// </summary>
        /// <param name="model">Model</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithModel(ChatModelType model)
        {
            Request.ModelId = model.ToModel().Id;
            _familyType = model.ToFamily();
            _modelType = model;
            return this;
        }
        /// <summary>
        /// ID of the model to use. You can use <see cref="IOpenAiModelApi.ListAsync()"/> to see all of your available models, or use a standard model like <see cref="Model.DavinciText"/>.
        /// </summary>
        /// <param name="modelId">Override with a custom model id</param>
        /// <param name="basedOnFamily">Family of your custom model</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithModel(string modelId, ModelFamilyType? basedOnFamily = null)
        {
            Request.ModelId = modelId;
            _forced = true;
            if (basedOnFamily != null)
                _familyType = basedOnFamily.Value;
            return this;
        }
        /// <summary>
        /// What sampling temperature to use, between 0 and 2. Higher values like 0.8 will make the output more random, while lower values like 0.2 will make it more focused and deterministic. We generally recommend altering this or Nucleus sampling but not both.
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithTemperature(double value)
        {
            if (value < 0)
                throw new ArgumentException("Temperature with a value lesser than 0");
            if (value > 2)
                throw new ArgumentException("Temperature with a value greater than 2");
            Request.Temperature = value;
            return this;
        }
        /// <summary>
        /// An alternative to sampling with temperature, called nucleus sampling, where the model considers the results of the tokens with top_p probability mass. So 0.1 means only the tokens comprising the top 10% probability mass are considered. It is generally recommend to use this or temperature but not both.
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithNucleusSampling(double value)
        {
            if (value < 0)
                throw new ArgumentException("Nucleus sampling with a value lesser than 0");
            if (value > 1)
                throw new ArgumentException("Nucleus sampling with a value greater than 1");
            Request.TopP = value;
            return this;
        }
        /// <summary>
        /// How many different choices to request for each prompt. Defaults to 1.
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithNumberOfChoicesPerPrompt(int value)
        {
            Request.NumberOfChoicesPerPrompt = value;
            return this;
        }
        /// <summary>
        /// One or more sequences where the API will stop generating further tokens. The returned text will not contain the stop sequence.
        /// </summary>
        /// <param name="values">Sequences</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithStopSequence(params string[] values)
        {
            if (values.Length > 1)
                Request.StopSequence = values;
            else if (values.Length == 1)
                Request.StopSequence = values[0];
            return this;
        }
        /// <summary>
        /// One or more sequences where the API will stop generating further tokens. The returned text will not contain the stop sequence.
        /// </summary>
        /// <param name="value">Sequences</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder AddStopSequence(string value)
        {
            if (Request.StopSequence == null)
                Request.StopSequence = value;
            else if (Request.StopSequence is string stringableSequence)
                Request.StopSequence = new string[2] { stringableSequence, value };
            else if (Request.StopSequence is string[] array)
            {
                var newArray = new string[array.Length + 1];
                array.CopyTo(newArray, 0);
                newArray[^1] = value;
                Request.StopSequence = newArray;
            }
            return this;
        }
        /// <summary>
        /// The maximum number of tokens allowed for the generated answer. By default, the number of tokens the model can return will be (4096 - prompt tokens).
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder SetMaxTokens(int value)
        {
            Request.MaxTokens = value;
            return this;
        }
        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on whether they appear in the text so far, increasing the model's likelihood to talk about new topics.
        /// <see href="https://platform.openai.com/docs/api-reference/parameter-details"></see>
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithPresencePenalty(double value)
        {
            if (value < -2)
                throw new ArgumentException("Presence penalty with a value lesser than -2");
            if (value > 2)
                throw new ArgumentException("Presence penalty with a value greater than 2");
            Request.PresencePenalty = value;
            return this;
        }
        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on their existing frequency in the text so far, decreasing the model's likelihood to repeat the same line verbatim.
        /// <see href="https://platform.openai.com/docs/api-reference/parameter-details"></see>
        /// </summary>
        /// <param name="value">Value</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithFrequencyPenalty(double value)
        {
            if (value < -2)
                throw new ArgumentException("Frequency penalty with a value lesser than -2");
            if (value > 2)
                throw new ArgumentException("Frequency penalty with a value greater than 2");
            Request.FrequencyPenalty = value;
            return this;
        }
        /// <summary>
        /// Modify the likelihood of specified tokens appearing in the completion.
        /// Accepts a json object that maps tokens (specified by their token ID in the tokenizer) to an associated bias value from -100 to 100. Mathematically, the bias is added to the logits generated by the model prior to sampling. The exact effect will vary per model, but values between -1 and 1 should decrease or increase likelihood of selection; values like -100 or 100 should result in a ban or exclusive selection of the relevant token.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithBias(string key, int value)
        {
            Request.Bias ??= new Dictionary<string, int>();
            if (!Request.Bias.ContainsKey(key))
                Request.Bias.Add(key, value);
            else
                Request.Bias[key] = value;
            return this;
        }
        /// <summary>
        /// Modify the likelihood of specified tokens appearing in the completion.
        /// Accepts a json object that maps tokens (specified by their token ID in the tokenizer) to an associated bias value from -100 to 100. Mathematically, the bias is added to the logits generated by the model prior to sampling. The exact effect will vary per model, but values between -1 and 1 should decrease or increase likelihood of selection; values like -100 or 100 should result in a ban or exclusive selection of the relevant token.
        /// </summary>
        /// <param name="bias"></param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithBias(Dictionary<string, int> bias)
        {
            foreach (var c in bias)
                WithBias(c.Key, c.Value);
            return this;
        }
        /// <summary>
        /// A unique identifier representing your end-user, which can help OpenAI to monitor and detect abuse.
        /// <see href="https://platform.openai.com/docs/guides/safety-best-practices/end-user-ids"></see>
        /// </summary>
        /// <param name="user">Unique identifier</param>
        /// <returns>Builder</returns>
        public ChatRequestBuilder WithUser(string user)
        {
            Request.User = user;
            return this;
        }
        /// <summary>
        /// Calculate the cost for this request based on configurated price during startup.
        /// </summary>
        /// <returns>decimal</returns>
        public decimal CalculateCost()
        {
            var tokenizer = Utility.Tokenizer.WithChatModel(_modelType);
            var promptTokens = 3;
            if (Request?.Messages != null)
                foreach (var message in Request.Messages)
                {
                    promptTokens += tokenizer.Encode(message.Content).NumberOfTokens + 5;
                }
            return CalculateCost(OpenAiType.Chat, new CompletionUsage
            {
                PromptTokens = promptTokens
            });
        }
    }
}
