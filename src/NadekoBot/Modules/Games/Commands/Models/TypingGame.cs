﻿using Discord;
using Discord.WebSocket;
using NadekoBot.Extensions;
using NadekoBot.Services;
using NadekoBot.Services.Games;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Games.Models
{
    public class TypingGame
    {
        public const float WORD_VALUE = 4.5f;
        public ITextChannel Channel { get; }
        public string CurrentSentence { get; private set; }
        public bool IsActive { get; private set; }
        private readonly Stopwatch sw;
        private readonly List<ulong> finishedUserIds;
        private readonly DiscordShardedClient _client;
        private readonly GamesService _games;
        private readonly string _prefix;

        private Logger _log { get; }

        public TypingGame(GamesService games, DiscordShardedClient client, ITextChannel channel, string prefix) //kek@prefix
        {
            _log = LogManager.GetCurrentClassLogger();
            _games = games;
            _client = client;
            _prefix = prefix;

            this.Channel = channel;
            IsActive = false;
            sw = new Stopwatch();
            finishedUserIds = new List<ulong>();
        }

        public async Task<bool> Stop()
        {
            if (!IsActive) return false;
            _client.MessageReceived -= AnswerReceived;
            finishedUserIds.Clear();
            IsActive = false;
            sw.Stop();
            sw.Reset();
            try { await Channel.SendConfirmAsync("Typing contest stopped.").ConfigureAwait(false); } catch (Exception ex) { _log.Warn(ex); }
            return true;
        }

        public async Task Start()
        {
            if (IsActive) return; // can't start running game
            IsActive = true;
            CurrentSentence = GetRandomSentence();
            var i = (int)(CurrentSentence.Length / WORD_VALUE * 1.7f);
            try
            {
                await Channel.SendConfirmAsync($@":clock2: Next contest will last for {i} seconds. Type the bolded text as fast as you can.").ConfigureAwait(false);


                var msg = await Channel.SendMessageAsync("Starting new typing contest in **3**...").ConfigureAwait(false);
                await Task.Delay(1000).ConfigureAwait(false);
                try
                {
                    await msg.ModifyAsync(m => m.Content = "Starting new typing contest in **2**...").ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                    await msg.ModifyAsync(m => m.Content = "Starting new typing contest in **1**...").ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                catch (Exception ex) { _log.Warn(ex); }

                await msg.ModifyAsync(m => m.Content = Format.Bold(Format.Sanitize(CurrentSentence.Replace(" ", " \x200B")).SanitizeMentions())).ConfigureAwait(false);
                sw.Start();
                HandleAnswers();

                while (i > 0)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    i--;
                    if (!IsActive)
                        return;
                }

            }
            catch { }
            finally
            {
                await Stop().ConfigureAwait(false);
            }
        }

        public string GetRandomSentence()
        {
            if (_games.TypingArticles.Any())
                return _games.TypingArticles[new NadekoRandom().Next(0, _games.TypingArticles.Count)].Text;
            else
                return $"No typing articles found. Use {_prefix}typeadd command to add a new article for typing.";

        }

        private void HandleAnswers()
        {
            _client.MessageReceived += AnswerReceived;
        }

        private Task AnswerReceived(SocketMessage imsg)
        {
            var _ = Task.Run(async () =>
            {
                try
                {
                    if (imsg.Author.IsBot)
                        return;
                    var msg = imsg as SocketUserMessage;
                    if (msg == null)
                        return;

                    if (this.Channel == null || this.Channel.Id != msg.Channel.Id) return;

                    var guess = msg.Content;

                    var distance = CurrentSentence.LevenshteinDistance(guess);
                    var decision = Judge(distance, guess.Length);
                    if (decision && !finishedUserIds.Contains(msg.Author.Id))
                    {
                        var elapsed = sw.Elapsed;
                        var wpm = CurrentSentence.Length / WORD_VALUE / elapsed.TotalSeconds * 60;
                        finishedUserIds.Add(msg.Author.Id);
                        await this.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                            .WithTitle($"{msg.Author} finished the race!")
                            .AddField(efb => efb.WithName("Place").WithValue($"#{finishedUserIds.Count}").WithIsInline(true))
                            .AddField(efb => efb.WithName("WPM").WithValue($"{wpm:F1} *[{elapsed.TotalSeconds:F2}sec]*").WithIsInline(true))
                            .AddField(efb => efb.WithName("Errors").WithValue(distance.ToString()).WithIsInline(true)))
                                .ConfigureAwait(false);
                        if (finishedUserIds.Count % 4 == 0)
                        {
                            await this.Channel.SendConfirmAsync($":exclamation: A lot of people finished, here is the text for those still typing:\n\n**{Format.Sanitize(CurrentSentence.Replace(" ", " \x200B")).SanitizeMentions()}**").ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex) { _log.Warn(ex); }
            });
            return Task.CompletedTask;
        }

        private bool Judge(int errors, int textLength) => errors <= textLength / 25;

    }
}