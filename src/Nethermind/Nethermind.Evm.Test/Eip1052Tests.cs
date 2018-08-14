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

using System.Numerics;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.Precompiles;
using NUnit.Framework;

namespace Nethermind.Evm.Test
{
    [TestFixture]
    public class Eip1052Tests : VirtualMachineTestsBase
    {
        protected override int BlockNumber => 6000000;

        [Test]
        public void Account_without_code_returns_empty_data_hash()
        {
            TestState.CreateAccount(TestObject.AddressC, 100.Ether());

            byte[] code = Prepare.EvmCode
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            TransactionReceipt receipt = Execute(code);
            AssertGas(receipt, GasCostOf.Transaction + GasCostOf.VeryLow * 2 + GasCostOf.SSet + GasCostOf.ExtCodeHash);
            AssertStorage(BigInteger.Zero, Keccak.OfAnEmptyString.Bytes);
        }

        [Test]
        public void Non_existing_account_returns_0()
        {
            byte[] code = Prepare.EvmCode
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            TransactionReceipt receipt = Execute(code);
            AssertGas(receipt,
                GasCostOf.Transaction + GasCostOf.VeryLow * 2 + GasCostOf.SReset + GasCostOf.ExtCodeHash);
            AssertStorage(BigInteger.Zero, Keccak.Zero);
        }

        [Test]
        public void Non_existing_precompile_returns_0()
        {
            Address precompileAddress =
                new Address(Sha256PrecompiledContract.Instance.Address.ToBigEndianByteArray(20));
            Assert.True(precompileAddress.IsPrecompiled(Spec));

            byte[] code = Prepare.EvmCode
                .PushData(precompileAddress)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            TransactionReceipt receipt = Execute(code);
            AssertStorage(BigInteger.Zero, Keccak.Zero);
        }

        [Test]
        public void Existing_precompile_returns_empty_data_hash()
        {
            Address precompileAddress =
                new Address(Sha256PrecompiledContract.Instance.Address.ToBigEndianByteArray(20));
            Assert.True(precompileAddress.IsPrecompiled(Spec));

            TestState.CreateAccount(precompileAddress, 1.Wei());

            byte[] code = Prepare.EvmCode
                .PushData(precompileAddress)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            TransactionReceipt receipt = Execute(code);
            AssertStorage(BigInteger.Zero, Keccak.OfAnEmptyString.Bytes);
        }

        [Test]
        public void Before_constantinople_throws_an_exception()
        {
            byte[] code = Prepare.EvmCode
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            TransactionReceipt receipt = Execute(1000000, 100000, code);
            Assert.AreEqual(StatusCode.Failure, receipt.StatusCode);
        }

        [Test]
        public void Addresses_are_trimmed_properly()
        {
            byte[] addressWithGarbage = ((byte[]) TestObject.AddressC.Hex).PadLeft(32);
            addressWithGarbage[11] = 88;

            TestState.CreateAccount(TestObject.AddressC, 1.Ether());
            Keccak codehash = Keccak.Compute("some code");
            TestState.UpdateCodeHash(TestObject.AddressC, codehash, Spec);

            byte[] code = Prepare.EvmCode
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .PushData(addressWithGarbage)
                .Op(Instruction.EXTCODEHASH)
                .PushData(1)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);
            AssertStorage(0, codehash.Bytes);
            AssertStorage(1, codehash.Bytes);
        }

        [Test]
        public void Self_destructed_returns_zero()
        {
            byte[] selfDestructCode = Prepare.EvmCode
                .PushData(B)
                .Op(Instruction.SELFDESTRUCT).Done;

            TestState.CreateAccount(TestObject.AddressC, 1.Ether());
            Keccak selfDestructCodeHash = TestState.UpdateCode(selfDestructCode);
            TestState.UpdateCodeHash(TestObject.AddressC, selfDestructCodeHash, Spec);

            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Call(TestObject.AddressC, 50000)
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);
            AssertStorage(0, Keccak.Zero);
        }

        [Test]
        public void Self_destructed_and_reverted_returns_code_hash()
        {
            byte[] callAndRevertCode = Prepare.EvmCode
                .Call(TestObject.AddressD, 50000)
                .Op(Instruction.REVERT).Done;

            byte[] selfDestructCode = Prepare.EvmCode
                .PushData(B)
                .Op(Instruction.SELFDESTRUCT).Done;

            TestState.CreateAccount(TestObject.AddressD, 1.Ether());
            Keccak selfDestructCodeHash = TestState.UpdateCode(selfDestructCode);
            TestState.UpdateCodeHash(TestObject.AddressD, selfDestructCodeHash, Spec);

            TestState.CreateAccount(TestObject.AddressC, 1.Ether());
            Keccak revertCodeHash = TestState.UpdateCode(callAndRevertCode);
            TestState.UpdateCodeHash(TestObject.AddressC, revertCodeHash, Spec);

            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Call(TestObject.AddressC, 50000)
                .PushData(TestObject.AddressD)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);
            AssertStorage(0, selfDestructCodeHash);
        }

        [Test]
        public void Empty_account_that_would_be_cleared_returns_zero()
        {
            TestState.CreateAccount(TestObject.AddressC, 0.Ether());

            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Call(TestObject.AddressC, 0)
                .PushData(TestObject.AddressC)
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);

            // todo: so far EIP does not define whether it should be zero or empty data
            AssertStorage(0, Keccak.OfAnEmptyString);
            Assert.False(TestState.AccountExists(TestObject.AddressC),
                "did not test the right thing - it was not an empty account + touch scenario");
        }

        [Test]
        public void Newly_created_empty_account_returns_empty_data_hash()
        {
            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Create(Bytes.Empty, 0)
                .PushData(Address.OfContract(B, 0))
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);

            // todo: so far EIP does not define whether it should be zero or empty data
            AssertStorage(0, Keccak.OfAnEmptyString);
            Assert.True(TestState.AccountExists(Address.OfContract(B, 0)),
                "did not test the right thing - it was not a newly created empty account scenario");
        }

        [Test]
        public void Create_and_revert_returns_zero()
        {
            byte[] deployedCode = {1, 2, 3};
            
            byte[] initCode = Prepare.EvmCode
                .PushData(deployedCode.PadRight(32))
                .PushData(0)
                .Op(Instruction.MSTORE)
                .PushData(3)
                .PushData(0)
                .Op(Instruction.RETURN)
                .Done;
            
            byte[] createCode = Prepare.EvmCode
                .Create(initCode, 0)
                .Op(Instruction.REVERT).Done;

            TestState.CreateAccount(TestObject.AddressC, 1.Ether());
            Keccak createCodeHash = TestState.UpdateCode(createCode);
            TestState.UpdateCodeHash(TestObject.AddressC, createCodeHash, Spec);

            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Call(TestObject.AddressC, 50000)
                .PushData(Address.OfContract(TestObject.AddressC, 0))
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);
            AssertStorage(0, Keccak.Zero);
        }
        
        [Test]
        public void Create_returns_code_hash()
        {
            byte[] deployedCode = {1, 2, 3};
            Keccak deployedCodeHash = Keccak.Compute(deployedCode);
            
            byte[] initCode = Prepare.EvmCode
                .PushData(deployedCode.PadRight(32))
                .PushData(0)
                .Op(Instruction.MSTORE)
                .PushData(3)
                .PushData(0)
                .Op(Instruction.RETURN)
                .Done;
            
            byte[] createCode = Prepare.EvmCode
                .Create(initCode, 0).Done;

            TestState.CreateAccount(TestObject.AddressC, 1.Ether());
            Keccak createCodeHash = TestState.UpdateCode(createCode);
            TestState.UpdateCodeHash(TestObject.AddressC, createCodeHash, Spec);

            IsTracingEnabled = true;
            byte[] code = Prepare.EvmCode
                .Call(TestObject.AddressC, 50000)
                .PushData(Address.OfContract(TestObject.AddressC, 0))
                .Op(Instruction.EXTCODEHASH)
                .PushData(0)
                .Op(Instruction.SSTORE)
                .Done;

            Execute(code);
            AssertStorage(0, deployedCodeHash);
        }
    }
}