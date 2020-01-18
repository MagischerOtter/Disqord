using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Disqord.Bot.Parsers;
using Disqord.Bot.Prefixes;
using Disqord.Events;
using Disqord.Logging;
using Qmmands;

namespace Disqord.Bot
{
    public abstract partial class DiscordBotBase : DiscordClientBase, IServiceProvider
    {
        public IPrefixProvider PrefixProvider { get; }

        private readonly CommandService _commandService;
        private readonly IServiceProvider _provider;

        internal DiscordBotBase(DiscordClientBase client, IPrefixProvider prefixProvider, DiscordBotConfiguration configuration) : base(client)
        {
            PrefixProvider = prefixProvider;
            configuration = configuration ?? DiscordBotConfiguration.Default;
            _commandService = configuration.CommandService ?? new CommandService();
            _provider = configuration.ProviderFactory?.Invoke(this);
            AddTypeParser(CachedRoleParser.Instance);
            AddTypeParser(CachedMemberParser.Instance);
            AddTypeParser(CachedUserParser.Instance);
            AddTypeParser(CachedGuildChannelParser<CachedGuildChannel>.Instance);
            AddTypeParser(CachedGuildChannelParser<CachedTextChannel>.Instance);
            AddTypeParser(CachedGuildChannelParser<CachedVoiceChannel>.Instance);
            AddTypeParser(CachedGuildChannelParser<CachedCategoryChannel>.Instance);
            AddTypeParser(LocalCustomEmojiParser.Instance);
            AddTypeParser(SnowflakeParser.Instance);
            AddTypeParser(ColorParser.Instance);
            //AddTypeParser(SanitaryContentParser.Instance);

            MessageReceived += MessageReceivedAsync;
        }

        protected virtual ValueTask<bool> CheckMessageAsync(CachedUserMessage message)
            => new ValueTask<bool>(IsBot
                ? !message.Author.IsBot
                : message.Author.Id == CurrentUser.Id);

        protected virtual ValueTask<bool> BeforeExecutedAsync(DiscordCommandContext context)
            => new ValueTask<bool>(true);

        protected virtual ValueTask AfterExecutedAsync(IResult result, DiscordCommandContext context)
            => default;

        protected virtual ValueTask<DiscordCommandContext> GetCommandContextAsync(CachedUserMessage message, IPrefix prefix)
            => new ValueTask<DiscordCommandContext>(new DiscordCommandContext(this, prefix, message));

        private async Task MessageReceivedAsync(MessageReceivedEventArgs args)
        {
            if (!(args.Message is CachedUserMessage message))
                return;

            try
            {
                if (!await CheckMessageAsync(message).ConfigureAwait(false))
                    return;
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while running the check message callback.", ex);
                return;
            }

            IEnumerable<IPrefix> prefixes;
            try
            {
                prefixes = await PrefixProvider.GetPrefixesAsync(message).ConfigureAwait(false);
                if (prefixes == null)
                    return;
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while getting the prefixes.", ex);
                return;
            }

            IPrefix foundPrefix = null;
            string output = null;
            try
            {
                foreach (var prefix in prefixes)
                {
                    if (prefix == null)
                    {
                        Log(LogMessageSeverity.Warning, "A null prefix was contained in the prefix enumerable.");
                        continue;
                    }

                    if (prefix.TryFind(message, out output))
                    {
                        foundPrefix = prefix;
                        break;
                    }
                }

                if (foundPrefix == null)
                    return;
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while finding the prefixes.", ex);
                return;
            }

            DiscordCommandContext context;
            try
            {
                context = await GetCommandContextAsync(message, foundPrefix).ConfigureAwait(false);
                if (context == null)
                    return;
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while getting the context.", ex);
                return;
            }

            try
            {
                if (!await BeforeExecutedAsync(context).ConfigureAwait(false))
                    return;
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while running the before executed callback.", ex);
                return;
            }

            var result = await _commandService.ExecuteAsync(output, context).ConfigureAwait(false);
            try
            {
                await AfterExecutedAsync(result, context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log(LogMessageSeverity.Error, "An exception occurred while running the after executed callback.", ex);
            }
        }

        public virtual object GetService(Type serviceType)
        {
            if (serviceType == typeof(DiscordBotBase) || serviceType == GetType())
                return this;

            return _provider?.GetService(serviceType);
        }


        /// <exception cref="TaskCanceledException"></exception>
        public void Run(CancellationToken cancellationToken = default)
            => RunAsync(cancellationToken).GetAwaiter().GetResult();

        internal new void Log(LogMessageSeverity severity, string message, Exception exception = null)
            => Logger.Log(this, new MessageLoggedEventArgs("Bot", severity, message, exception));

        public override ValueTask DisposeAsync()
        {
            MessageReceived -= MessageReceivedAsync;
            (_provider as IDisposable)?.Dispose();
            return base.DisposeAsync();
        }
    }
}