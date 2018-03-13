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

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Network.Crypto;
using Org.BouncyCastle.Crypto.Digests;

namespace Nethermind.Network.Rlpx.Handshake
{
    /// <summary>
    ///     https://github.com/ethereum/devp2p/blob/master/rlpx.md
    /// </summary>
    public class EncryptionHandshakeService : IEncryptionHandshakeService
    {
        private static int MacBitsSize = 256;

        private readonly ICryptoRandom _cryptoRandom;
        private readonly IEciesCipher _eciesCipher;
        private readonly IMessageSerializationService _messageSerializationService;
        private readonly PrivateKey _privateKey;
        private readonly ISigner _signer;

        public EncryptionHandshakeService(
            IMessageSerializationService messageSerializationService,
            IEciesCipher eciesCipher,
            ICryptoRandom cryptoRandom,
            ISigner signer,
            PrivateKey privateKey,
            ILogger logger)
        {
            _messageSerializationService = messageSerializationService;
            _eciesCipher = eciesCipher;
            _privateKey = privateKey;
            _cryptoRandom = cryptoRandom;
            _signer = signer;
        }

        public Packet Auth(PublicKey remoteNodePublicKey, EncryptionHandshake handshake)
        {
            handshake.RemotePublicKey = remoteNodePublicKey;
            handshake.InitiatorNonce = _cryptoRandom.GenerateRandomBytes(32);
            handshake.EphemeralPrivateKey = new PrivateKey(_cryptoRandom.GenerateRandomBytes(32));

            byte[] staticSharedSecret = BouncyCrypto.Agree(_privateKey, remoteNodePublicKey);
            byte[] forSigning = staticSharedSecret.Xor(handshake.InitiatorNonce);

            AuthEip8Message authMessage = new AuthEip8Message();
            authMessage.Nonce = handshake.InitiatorNonce;
            authMessage.PublicKey = _privateKey.PublicKey;
            authMessage.Signature = _signer.Sign(handshake.EphemeralPrivateKey, new Keccak(forSigning));

            byte[] authData = _messageSerializationService.Serialize(authMessage);
            int size = authData.Length + 32 + 16 + 65; // data + MAC + IV + pub
            byte[] sizeBytes = size.ToBigEndianByteArray().Slice(2, 2);
            byte[] packetData = _eciesCipher.Encrypt(
                remoteNodePublicKey,
                authData,
                sizeBytes);

            handshake.AuthPacket = new Packet(Bytes.Concat(sizeBytes, packetData));
            return handshake.AuthPacket;
        }

        public Packet Ack(EncryptionHandshake handshake, Packet auth)
        {
            handshake.AuthPacket = auth;

            // TODO: try, retry (support old clients)
            byte[] sizeData = auth.Data.Slice(0, 2);
            byte[] plaintext = _eciesCipher.Decrypt(_privateKey, auth.Data.Slice(2), sizeData);
            AuthMessageBase authMessage = _messageSerializationService.Deserialize<AuthEip8Message>(plaintext);

            handshake.RemotePublicKey = authMessage.PublicKey;
            handshake.RecipientNonce = _cryptoRandom.GenerateRandomBytes(32);
            handshake.EphemeralPrivateKey = new PrivateKey(_cryptoRandom.GenerateRandomBytes(32));

            handshake.InitiatorNonce = authMessage.Nonce;
            byte[] staticSharedSecret = BouncyCrypto.Agree(_privateKey, handshake.RemotePublicKey);
            byte[] forSigning = staticSharedSecret.Xor(handshake.InitiatorNonce);
            
            handshake.RemoteEphemeralPublicKey = _signer.RecoverPublicKey(authMessage.Signature, new Keccak(forSigning));

            // TODO: respond depending on the auth type
            AckEip8Message ackMessage = new AckEip8Message();
            ackMessage.EphemeralPublicKey = handshake.EphemeralPrivateKey.PublicKey;
//            responseMessage.IsTokenUsed = false;
            ackMessage.Nonce = handshake.RecipientNonce;
            
            byte[] ackData = _messageSerializationService.Serialize(ackMessage);
            int size = ackData.Length + 32 + 16 + 65; // data + MAC + IV + pub
            byte[] sizeBytes = size.ToBigEndianByteArray().Slice(2, 2);
            byte[] packetData = _eciesCipher.Encrypt(handshake.RemotePublicKey, ackData, sizeBytes);
            handshake.AckPacket = new Packet(Bytes.Concat(sizeBytes, packetData));
            SetSecrets(handshake, EncryptionHandshakeRole.Recipient);
            return handshake.AckPacket;
        }

        public void Agree(EncryptionHandshake handshake, Packet ack)
        {
            handshake.AckPacket = ack;

            // TODO: try, retry (support old clients)
            byte[] sizeData = ack.Data.Slice(0, 2);
            byte[] plaintext = _eciesCipher.Decrypt(_privateKey, ack.Data.Slice(2), sizeData);
            AckEip8Message ackMessage = _messageSerializationService.Deserialize<AckEip8Message>(plaintext);

            handshake.RemoteEphemeralPublicKey = ackMessage.EphemeralPublicKey;
            handshake.RecipientNonce = ackMessage.Nonce;

            SetSecrets(handshake, EncryptionHandshakeRole.Initiator);
        }

        private static void SetSecrets(EncryptionHandshake handshake, EncryptionHandshakeRole encryptionHandshakeRole)
        {
            byte[] ephemeralSharedSecret = BouncyCrypto.Agree(handshake.EphemeralPrivateKey, handshake.RemoteEphemeralPublicKey);
            byte[] nonceHash = Keccak.Compute(Bytes.Concat(handshake.RecipientNonce, handshake.InitiatorNonce)).Bytes;
            byte[] sharedSecret = Keccak.Compute(Bytes.Concat(ephemeralSharedSecret, nonceHash)).Bytes;
            byte[] token = Keccak.Compute(sharedSecret).Bytes;
            byte[] aesSecret = Keccak.Compute(Bytes.Concat(ephemeralSharedSecret, sharedSecret)).Bytes;
            Array.Clear(sharedSecret, 0, sharedSecret.Length); // TODO: it was passed in the concat for Keccak so not good enough
            byte[] macSecret = Keccak.Compute(Bytes.Concat(ephemeralSharedSecret, aesSecret)).Bytes;
            Array.Clear(ephemeralSharedSecret, 0, ephemeralSharedSecret.Length); // TODO: it was passed in the concat for Keccak so not good enough
            handshake.Secrets = new EncryptionSecrets();
            handshake.Secrets.Token = token;
            handshake.Secrets.AesSecret = aesSecret;
            handshake.Secrets.MacSecret = macSecret;

            KeccakDigest mac1 = new KeccakDigest(MacBitsSize);
            mac1.BlockUpdate(macSecret.Xor(handshake.RecipientNonce), 0, macSecret.Length);
            mac1.BlockUpdate(handshake.AuthPacket.Data, 0, handshake.AuthPacket.Data.Length);

            KeccakDigest mac2 = new KeccakDigest(MacBitsSize);
            mac2.BlockUpdate(macSecret.Xor(handshake.InitiatorNonce), 0, macSecret.Length);
            mac2.BlockUpdate(handshake.AckPacket.Data, 0, handshake.AckPacket.Data.Length);

            if (encryptionHandshakeRole == EncryptionHandshakeRole.Initiator)
            {
                handshake.Secrets.EgressMac = mac1;
                handshake.Secrets.IngressMac = mac2;
            }
            else
            {
                handshake.Secrets.EgressMac = mac2;
                handshake.Secrets.IngressMac = mac1;
            }
        }
    }
}