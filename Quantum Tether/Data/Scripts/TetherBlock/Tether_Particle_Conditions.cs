using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using SpaceEngineers.ObjectBuilders.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace InventoryTether.Particle
{
    public abstract partial class StandardParticleGamelogic
    {
        ParticleBase CreateParticleHolder(IMyModelDummy dummy, string particleSubtypeId, string condition = null)
        {
            switch(condition)
            {
                case "working": // only shows particle if block is functional+enabled+powered
                    return new ParticleOnWorking(this, particleSubtypeId, dummy.Matrix);
                case "enablednonworking":
                    return new ParticleOnEnabled(this, particleSubtypeId, dummy.Matrix);
            }

            return new ParticleBase(this, particleSubtypeId, dummy.Matrix);
        }
    }

    public class ParticleBase
    {
        public StandardParticleGamelogic GameLogic;
        public string SubtypeId;
        public MatrixD LocalMatrix;
        public MyParticleEffect Effect;

        public ParticleBase(StandardParticleGamelogic gamelogic, string subtypeId, MatrixD localMatrix)
        {
            GameLogic = gamelogic;
            SubtypeId = subtypeId;
            LocalMatrix = localMatrix;

            Effect = SpawnParticle();
            if(Effect == null)
                throw new Exception($"Couldn't spawn particle: {subtypeId}");
        }

        public virtual void Close()
        {
            if(Effect != null)
                MyParticlesManager.RemoveParticleEffect(Effect);
        }

        protected virtual MyParticleEffect SpawnParticle()
        {
            MyParticleEffect effect;
            Vector3D worldPos = GameLogic.Entity.GetPosition();
            uint parentId = GameLogic.Entity.Render.GetRenderObjectID();
            if(!MyParticlesManager.TryCreateParticleEffect(SubtypeId, ref LocalMatrix, ref worldPos, parentId, out effect))
                return null;

            return effect;
        }
    }

    public class ParticleOnWorking : ParticleBase, IUpdateable
    {
        public readonly IMyFunctionalBlock Block;

        public ParticleOnWorking(StandardParticleGamelogic gamelogic, string subtypeId, MatrixD localMatrix) : base(gamelogic, subtypeId, localMatrix)
        {
            Block = gamelogic.Entity as IMyFunctionalBlock;
            if(Block == null)
                throw new Exception($"{GetType().Name}: Unsupported block type, needs on/off");
        }

        // frequency dictated by the gamelogic, currently it's every 10th tick (approx, they're spread out to run as few per tick as possible)
        public void Update()
        {
            if(Block == null)
                return;

            bool currentState = Effect != null;
            bool targetState = Block.Enabled && Block.IsWorking;

            if(targetState != currentState)
            {
                if(targetState)
                {
                    Effect = SpawnParticle();
                }
                else
                {
                    Effect.Stop();
                    Effect = null;
                }
            }
        }
    }

    public class ParticleOnEnabled : ParticleBase, IUpdateable
    {
        public readonly IMyFunctionalBlock Block;

        public ParticleOnEnabled(StandardParticleGamelogic gamelogic, string subtypeId, MatrixD localMatrix) : base(gamelogic, subtypeId, localMatrix)
        {
            Block = gamelogic.Entity as IMyFunctionalBlock;
            if (Block == null)
                throw new Exception($"{GetType().Name}: Unsupported block type, needs on/off");
        }

        // frequency dictated by the gamelogic, currently it's every 10th tick (approx, they're spread out to run as few per tick as possible)
        public void Update()
        {
            if (Block == null)
                return;

            bool currentState = Effect != null;
            bool targetState = Block.Enabled && !Block.IsWorking;

            if (targetState != currentState)
            {
                if (targetState)
                {
                    Effect = SpawnParticle();
                }
                else
                {
                    Effect.Stop();
                    Effect = null;
                }
            }
        }
    }
}
