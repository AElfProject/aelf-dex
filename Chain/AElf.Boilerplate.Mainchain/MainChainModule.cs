﻿using System;
using System.Collections.Generic;
using AElf;
using AElf.Blockchains.BasicBaseChain;
using AElf.Consensus.DPoS;
using AElf.Contracts.Consensus.DPoS;
using AElf.Contracts.Dividend;
using AElf.Contracts.Genesis;
using AElf.Contracts.MultiToken;
using AElf.Contracts.MultiToken.Messages;
using AElf.CrossChain.Grpc;
//using AElf.Contracts.Resource;
//using AElf.Contracts.Resource.FeeReceiver;
using AElf.Kernel;
using AElf.Kernel.Consensus;
using AElf.Kernel.Consensus.DPoS;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.Token;
using AElf.Modularity;
using AElf.OS;
using AElf.OS.Network.Grpc;
using AElf.OS.Node.Application;
using AElf.OS.Node.Domain;
using AElf.OS.Rpc.ChainController;
using AElf.OS.Rpc.Net;
using AElf.OS.Rpc.Wallet;
using AElf.Runtime.CSharp;
using AElf.RuntimeSetup;
using AElf.WebApp.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.AspNetCore;
using Volo.Abp.Modularity;
using Volo.Abp.Threading;

namespace Aelf.Boilerplate.Mainchain
{
    [DependsOn(
        typeof(DPoSConsensusAElfModule),
        typeof(KernelAElfModule),
        typeof(OSAElfModule),
        typeof(AbpAspNetCoreModule),
        typeof(CSharpRuntimeAElfModule),
        typeof(GrpcNetworkModule),

        //TODO: should move to OSAElfModule
        typeof(ChainControllerRpcModule),
        typeof(WalletRpcModule),
        typeof(NetRpcAElfModule),
        typeof(RuntimeSetupAElfModule),
        
        //web api module
        typeof(WebWebAppAElfModule)
    )]
    public class MainChainModule : AElfModule
    {
        public ILogger<MainChainModule> Logger { get; set; }

        public OsBlockchainNodeContext OsBlockchainNodeContext { get; set; }

        public MainChainModule()
        {
            Logger = NullLogger<MainChainModule>.Instance;
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var chainOptions = context.ServiceProvider.GetService<IOptionsSnapshot<ChainOptions>>().Value;
            var dto = new OsBlockchainNodeContextStartDto()
            {
                ChainId = chainOptions.ChainId,
                ZeroSmartContract = typeof(BasicContractZero)
            };
            
            var dposOptions = context.ServiceProvider.GetService<IOptionsSnapshot<DPoSOptions>>().Value;
            var zeroContractAddress = context.ServiceProvider.GetRequiredService<ISmartContractAddressService>()
                .GetZeroSmartContractAddress();

            dto.InitializationSmartContracts.AddConsensusSmartContract<ConsensusContract>(
                GenerateConsensusInitializationCallList(dposOptions));
            
            Logger.LogTrace(Hash.FromString("Hash").ToHex());
            
            dto.InitializationSmartContracts
                .AddGenesisSmartContract<HelloWorldContract.HelloWorldContract>(Hash.FromString("Hash"));

//            
//            dto.InitializationSmartContracts.AddGenesisSmartContract<TokenContract>(
//                TokenSmartContractAddressNameProvider.Name,
//                GenerateTokenInitializationCallList(zeroContractAddress,
//                    context.ServiceProvider.GetService<IOptions<DPoSOptions>>().Value.InitialMiners));


            var osService = context.ServiceProvider.GetService<IOsBlockchainNodeContextService>();
            var that = this;
            AsyncHelper.RunSync(async () => { that.OsBlockchainNodeContext = await osService.StartAsync(dto); });
        }

        private SystemTransactionMethodCallList GenerateConsensusInitializationCallList(DPoSOptions dposOptions)
        {
            var consensusMethodCallList = new SystemTransactionMethodCallList();
            consensusMethodCallList.Add(nameof(ConsensusContract.InitialDPoSContract),
                new InitialDPoSContractInput
                {
                    TokenContractSystemName = TokenSmartContractAddressNameProvider.Name,
                    DividendsContractSystemName = DividendsSmartContractAddressNameProvider.Name,
                    LockTokenForElection = 10_0000
                });
            consensusMethodCallList.Add(nameof(ConsensusContract.InitialConsensus),
                dposOptions.InitialMiners.ToMiners().GenerateFirstRoundOfNewTerm(dposOptions.MiningInterval,
                    dposOptions.StartTimestamp.ToUniversalTime()));
            consensusMethodCallList.Add(nameof(ConsensusContract.ConfigStrategy),
                new DPoSStrategyInput
                {
                    IsBlockchainAgeSettable = dposOptions.IsBlockchainAgeSettable,
                    IsTimeSlotSkippable = dposOptions.IsTimeSlotSkippable,
                    IsVerbose = dposOptions.Verbose
                });
            return consensusMethodCallList;
        }
        
//        private SystemTransactionMethodCallList GenerateDividendInitializationCallList()
//        {
//            var dividendMethodCallList = new SystemTransactionMethodCallList();
//            dividendMethodCallList.Add(nameof(DividendContract.InitializeDividendContract),
//                new InitialDividendContractInput
//                {
//                    ConsensusContractSystemName = ConsensusSmartContractAddressNameProvider.Name,
//                    TokenContractSystemName = TokenSmartContractAddressNameProvider.Name
//                });
//            return dividendMethodCallList;
//        }
//
//        private SystemTransactionMethodCallList GenerateTokenInitializationCallList(Address issuer,
//            List<string> tokenReceivers)
//        {
//            const string symbol = "ELF";
//            const int totalSupply = 10_0000_0000;
//            var tokenContractCallList = new SystemTransactionMethodCallList();
//            tokenContractCallList.Add(nameof(TokenContract.CreateNativeToken), new CreateNativeTokenInput
//            {
//                Symbol = symbol,
//                Decimals = 2,
//                IsBurnable = true,
//                TokenName = "elf token",
//                TotalSupply = totalSupply,
//                // Set the contract zero address as the issuer temporarily.
//                Issuer = issuer,
//                LockWhiteSystemContractNameList = {ConsensusSmartContractAddressNameProvider.Name}
//            });
//
//            tokenContractCallList.Add(nameof(TokenContract.IssueNativeToken), new IssueNativeTokenInput
//            {
//                Symbol = symbol,
//                Amount = (long) (totalSupply * 0.2),
//                ToSystemContractName = DividendsSmartContractAddressNameProvider.Name,
//                Memo = "Set dividends.",
//            });
//
//            //TODO: Maybe should be removed after testing.
//            foreach (var tokenReceiver in tokenReceivers)
//            {
//                tokenContractCallList.Add(nameof(TokenContract.Issue), new IssueInput
//                {
//                    Symbol = symbol,
//                    Amount = (long) (totalSupply * 0.8) / tokenReceivers.Count,
//                    To = Address.FromPublicKey(ByteArrayHelpers.FromHexString(tokenReceiver)),
//                    Memo = "Set initial miner's balance.",
//                });
//            }
//
//            // Set fee pool address to dividend contract address.
//            tokenContractCallList.Add(nameof(TokenContract.SetFeePoolAddress),
//                DividendsSmartContractAddressNameProvider.Name);
//
//            tokenContractCallList.Add(nameof(TokenContract.InitializeTokenContract), new IntializeTokenContractInput
//            {
//                CrossChainContractSystemName = CrossChainSmartContractAddressNameProvider.Name
//            });
//            return tokenContractCallList;
//        }

        public override void OnApplicationShutdown(ApplicationShutdownContext context)
        {
            var osService = context.ServiceProvider.GetService<IOsBlockchainNodeContextService>();
            var that = this;
            AsyncHelper.RunSync(() => osService.StopAsync(that.OsBlockchainNodeContext));
        }
    }
}