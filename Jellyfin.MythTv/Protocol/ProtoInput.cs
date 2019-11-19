﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoInput : ProtoBase
    {

        public ProtoInput(string server, int port, ILogger logger) : base (server, port, AnnounceModeType.Monitor, EventModeType.None, logger) {}

         public async Task<Input> GetFreeInputAsync()
        {
            var inputs = await GetFreeInputsAsync().ConfigureAwait(false);

            return inputs.Count > 0 ? inputs[0] : null;
        }

        public async Task<List<Input>> GetFreeInputsAsync()
        {
            if (ProtoVersion >= 91) return await GetFreeInputs91Async().ConfigureAwait(false);
            if (ProtoVersion >= 90) return await GetFreeInputs90Async().ConfigureAwait(false);
            if (ProtoVersion >= 89) return await GetFreeInputs89Async().ConfigureAwait(false);
            return await GetFreeInputs87Async().ConfigureAwait(false);;
        }

        private async Task<List<Input>> GetFreeInputs87Async()
        {
            var input = await SendCommandAsync("GET_FREE_INPUT_INFO 0").ConfigureAwait(false);
            var output = new List<Input>();

            if (input.Count == 0)
                return output;

            // each card has 11 fields
            if (input.Count % 11 != 0)
                throw new Exception("Expected multiple of 11 fields in GET_FREE_INPUT_INFO response");

            for (int i = 0; i < input.Count; i += 11)
            {
                var curr = input.GetRange(i, 11);
                var card = new Input();

                card.InputName = curr[0];
                card.SourceId = int.Parse(curr[1]);
                card.Id = int.Parse(curr[2]);
                card.CardId = int.Parse(curr[3]);
                card.MplexId = int.Parse(curr[4]);
                card.LiveTVOrder = int.Parse(curr[5]);

                output.Add(card);
            }

            return output;
        }

        private async Task<List<Input>> GetFreeInputs89Async()
        {
            var input = await SendCommandAsync("GET_FREE_INPUT_INFO 0").ConfigureAwait(false);
            var output = new List<Input>();

            if (input.Count == 0)
                return output;

            // each card has 12 fields
            if (input.Count % 12 != 0)
                throw new Exception("Expected multiple of 12 fields in GET_FREE_INPUT_INFO response");

            for (int i = 0; i < input.Count; i += 12)
            {
                var curr = input.GetRange(i, 12);
                var card = new Input();

                card.InputName = curr[0];
                card.SourceId = int.Parse(curr[1]);
                card.Id = int.Parse(curr[2]);
                card.CardId = int.Parse(curr[3]);
                card.MplexId = int.Parse(curr[4]);
                card.LiveTVOrder = int.Parse(curr[5]);

                output.Add(card);
            }

            return output;
        }

        private async Task<List<Input>> GetFreeInputs90Async()
        {
            var input = await SendCommandAsync("GET_FREE_INPUT_INFO 0").ConfigureAwait(false);
            var output = new List<Input>();

            if (input.Count == 0)
                return output;

            // each card has 12 fields
            if (input.Count % 12 != 0)
                throw new Exception("Expected multiple of 12 fields in GET_FREE_INPUT_INFO response");

            for (int i = 0; i < input.Count; i += 12)
            {
                var curr = input.GetRange(i, 12);
                var card = new Input();

                card.InputName = curr[0];
                card.SourceId = int.Parse(curr[1]);
                card.Id = int.Parse(curr[2]);
                card.CardId = card.Id;
                card.MplexId = int.Parse(curr[4]);
                card.LiveTVOrder = int.Parse(curr[5]);

                output.Add(card);
            }

            return output;
        }

        private async Task<List<Input>> GetFreeInputs91Async()
        {
            var input = await SendCommandAsync("GET_FREE_INPUT_INFO 0").ConfigureAwait(false);
            var output = new List<Input>();

            if (input.Count == 0)
                return output;

            // each card has 10 fields
            if (input.Count % 10 != 0)
                throw new Exception("Expected multiple of 10 fields in GET_FREE_INPUT_INFO response");

            for (int i = 0; i < input.Count; i += 10)
            {
                var curr = input.GetRange(i, 10);
                var card = new Input();

                card.InputName = curr[0];
                card.SourceId = int.Parse(curr[1]);
                card.Id = int.Parse(curr[2]);
                card.CardId = card.Id;
                card.MplexId = int.Parse(curr[3]);
                card.LiveTVOrder = int.Parse(curr[4]);

                output.Add(card);
            }

            return output;
        }
    }


}
