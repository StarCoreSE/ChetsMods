using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace InventoryTether.Animation
{
    // Edit the block type and subtypes to match your custom block.
    // For type always use the same name as the <TypeId> and append "MyObjectBuilder_" to it, don't use the one from xsi:type.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class TetherRingsAnimation : MyGameLogicComponent
    {
        private const string SubpartOneName = "Torus_1"; // dummy name without the "subpart_" prefix
        private const float SubOne_DegreesPerTick = 10f; // rotation per tick in degrees (60 ticks per second)
        private const float SubOne_AccelPercentPerTick = 0.05f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubOne_DeaccelPercentPerTick = 0.0035f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubOne_RotAxis = Vector3.Forward; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubOne_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private const string SubpartTwoName = "Torus_2"; // dummy name without the "subpart_" prefix
        private const float SubTwo_DegreesPerTick = 7.5f; // rotation per tick in degrees (60 ticks per second)
        private const float SubTwo_AccelPercentPerTick = 0.035f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubTwo_DeaccelPercentPerTick = 0.005f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubTwo_RotAxis = Vector3.Left; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubTwo_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private const string SubpartThreeName = "Torus_3"; // dummy name without the "subpart_" prefix
        private const float SubThree_DegreesPerTick = 5f; // rotation per tick in degrees (60 ticks per second)
        private const float SubThree_AccelPercentPerTick = 0.025f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubThree_DeaccelPercentPerTick = 0.0075f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubThree_RotAxis = Vector3.Up; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubThree_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private IMyFunctionalBlock block;

        private bool SubOneFirstFind = true;
        private Matrix SubOneLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubOneTargetSpeedMultiplier; // used for smooth transition

        private bool SubTwoFirstFind = true;
        private Matrix SubTwoLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubTwoTargetSpeedMultiplier; // used for smooth transition

        private bool SubThreeFoundFirst = true;
        private Matrix SubThreeLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubThreeTargetSpeedMultiplier; // used for smooth transition

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if(MyAPIGateway.Utilities.IsDedicated)
                return;

            block = (IMyFunctionalBlock)Entity;

            if(block.CubeGrid?.Physics == null)
                return;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                SpinSubpartOne();
                SpinSubpartTwo();
                SpinSubpartThree();

            }
            catch(Exception e)
            {
                AddToLog(e);
            }
        }

        private void SpinSubpartOne()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubOneTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubOneTargetSpeedMultiplier < 1)
            {
                SubOneTargetSpeedMultiplier = Math.Min(SubOneTargetSpeedMultiplier + SubOne_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubOneTargetSpeedMultiplier > 0)
            {
                SubOneTargetSpeedMultiplier = Math.Max(SubOneTargetSpeedMultiplier - SubOne_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubOne_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartOneName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubOneFirstFind) // first time the subpart was found
                {
                    SubOneFirstFind = false;
                    SubOneLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubOneTargetSpeedMultiplier > 0)
                {
                    SubOneLocalMatrix *= Matrix.CreateFromAxisAngle(SubOne_RotAxis, MathHelper.ToRadians(SubOneTargetSpeedMultiplier * SubOne_DegreesPerTick));
                    SubOneLocalMatrix = Matrix.Normalize(SubOneLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubOneLocalMatrix);
            }
        }

        private void SpinSubpartTwo()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubTwoTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubTwoTargetSpeedMultiplier < 1)
            {
                SubTwoTargetSpeedMultiplier = Math.Min(SubTwoTargetSpeedMultiplier + SubTwo_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubTwoTargetSpeedMultiplier > 0)
            {
                SubTwoTargetSpeedMultiplier = Math.Max(SubTwoTargetSpeedMultiplier - SubTwo_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubTwo_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartTwoName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubTwoFirstFind) // first time the subpart was found
                {
                    SubTwoFirstFind = false;
                    SubTwoLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubTwoTargetSpeedMultiplier > 0)
                {
                    SubTwoLocalMatrix *= Matrix.CreateFromAxisAngle(SubTwo_RotAxis, MathHelper.ToRadians(SubTwoTargetSpeedMultiplier * SubTwo_DegreesPerTick));
                    SubTwoLocalMatrix = Matrix.Normalize(SubTwoLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubTwoLocalMatrix);
            }
        }

        private void SpinSubpartThree()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubThreeTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubThreeTargetSpeedMultiplier < 1)
            {
                SubThreeTargetSpeedMultiplier = Math.Min(SubThreeTargetSpeedMultiplier + SubThree_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubThreeTargetSpeedMultiplier > 0)
            {
                SubThreeTargetSpeedMultiplier = Math.Max(SubThreeTargetSpeedMultiplier - SubThree_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubThree_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartThreeName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubThreeFoundFirst) // first time the subpart was found
                {
                    SubThreeFoundFirst = false;
                    SubThreeLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubThreeTargetSpeedMultiplier > 0)
                {
                    SubThreeLocalMatrix *= Matrix.CreateFromAxisAngle(SubThree_RotAxis, MathHelper.ToRadians(SubThreeTargetSpeedMultiplier * SubThree_DegreesPerTick));
                    SubThreeLocalMatrix = Matrix.Normalize(SubThreeLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubThreeLocalMatrix);
            }
        }

        private void AddToLog(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"ERROR {GetType().FullName}: {e.ToString()}");

            if(MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ ERROR: {GetType().FullName}: {e.Message} | Send SpaceEngineers.Log to mod author ]", 10000, MyFontEnum.Red);
        }
    }
}
