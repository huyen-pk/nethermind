﻿/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using Nethermind.Core.Encoding;

namespace Nethermind.Network.P2P.Subprotocols.Eth
{
    public class StatusMessageSerializer : IMessageSerializer<StatusMessage>
    {
        public byte[] Serialize(StatusMessage message)
        {
            return Rlp.Encode(
                message.ProtocolVersion,
                message.NetworkId,
                message.TotalDifficulty,
                message.BestHash,
                message.GenesisHash
            ).Bytes;
        }

        public StatusMessage Deserialize(byte[] bytes)
        {
            StatusMessage statusMessage = new StatusMessage();
            DecodedRlp decoded = Rlp.Decode(new Rlp(bytes));
            statusMessage.ProtocolVersion = decoded.GetByte(0);
            statusMessage.NetworkId = decoded.GetInt(1);
            statusMessage.TotalDifficulty = decoded.GetUnsignedBigInteger(2);
            statusMessage.BestHash = decoded.GetKeccak(3);
            statusMessage.GenesisHash = decoded.GetKeccak(4);
            return statusMessage;
        }
    }
}