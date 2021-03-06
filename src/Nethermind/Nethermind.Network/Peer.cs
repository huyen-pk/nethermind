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

using Nethermind.Blockchain;
using Nethermind.Network.P2P;
using Nethermind.Stats;
using Nethermind.Stats.Model;

namespace Nethermind.Network
{
    public class Peer
    {
        public Peer(Node node, INodeStats nodeStats, ConnectionDirection connectionDirection)
        {
            Node = node;
            NodeStats = nodeStats;
            ConnectionDirection = connectionDirection;
        }

        public Node Node { get; }
        public bool AddedToDiscovery { get; set; }
        public INodeStats NodeStats { get; }
        public IP2PSession Session { get; set; }
        public ISynchronizationPeer SynchronizationPeer { get; set; }
        public IP2PMessageSender P2PMessageSender { get; set; }
        public ConnectionDirection ConnectionDirection { get; set; }
    }
}