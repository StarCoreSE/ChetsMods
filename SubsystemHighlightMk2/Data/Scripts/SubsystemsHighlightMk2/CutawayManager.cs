using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace StarCore.Highlights
{
    public class CutawayManager
    {
        public static CutawayManager I = new CutawayManager();

        public enum CutawayAxisEnum
        {
            XAxis = 0,
            YAxis = 1,
            ZAxis = 2,
        }
        public CutawayAxisEnum CutawayAxis;
        public float CutawayPosition;

        private PlaneD cutawayPlane;
        private Vector3D cutawayPlanePosition;

        public IMyCubeGrid cachedGrid = null;
        public List<IMySlimBlock> cachedGridBlocks = new List<IMySlimBlock>();
        public bool IsNormalInverted = false;

        public bool StopDraw = true;

        #region Update Methods
        public void Init()
        {
            I = this;

            CutawayPosition = (float)1.25;
        }

        public void Update(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                ClearCache();
                return ;
            }

            if (cachedGrid == null || grid.EntityId != cachedGrid.EntityId)
            {
                ClearCache(false, grid);

                StopDraw = false;

                cachedGrid = grid;
                cachedGridBlocks.Clear();
                grid.GetBlocks(cachedGridBlocks);
                UpdateBlocks(grid);
            }         
        }

        public void Draw()
        {
            if (cutawayPlane == null || cachedGrid == null)
                return;

            if (StopDraw)
                return;

            if (MyAPIGateway.Session.Config.HudState == 0)
                return;

            DrawCutawayPlaneBillboard(cutawayPlane, cutawayPlanePosition, cachedGrid, CutawayAxis, Color.Red);
        }

        public void Unload()
        {
            ClearCache();
            I = null;
        }
        #endregion

        public void UpdateBlocks(IMyCubeGrid grid)
        {
            StopDraw = false;

            Vector3D gridCenter = grid.PositionComp.WorldAABB.Center;
            MatrixD gridOrientation = grid.WorldMatrix;
            Vector3D offsetVector = GetOffsetVector(gridOrientation);

            cutawayPlanePosition = gridCenter + offsetVector;
            cutawayPlane = new PlaneD(cutawayPlanePosition, GetPlaneNormal(gridOrientation));

            LimitCutawayPosition(grid, CutawayAxis, ref CutawayPosition);

            foreach (var block in cachedGridBlocks)
            {
                Vector3D blockCenter;
                block.ComputeWorldCenter(out blockCenter);

                if (SignedDistanceToPoint(cutawayPlane, blockCenter) < 0)
                {
                    IsVisible(block, false);
                }
                else
                {
                    IsVisible(block, true);
                }
            }
        }

        #region Utils
        private void LimitCutawayPosition(IMyCubeGrid grid, CutawayAxisEnum currentAxis, ref float cutawayPosition)
        {
            /*BoundingBox localAABB = grid.LocalAABB;
            Vector3 halfExtents = (localAABB.Max - localAABB.Min) * 0.5f;
            Vector3 center = (localAABB.Max + localAABB.Min) * 0.5f;

            *//*var min = grid.LocalAABB.Min / 2;
            var max = grid.LocalAABB.Max / 2;*//*

            double minBound, maxBound;

            switch (currentAxis)
            {
                case CutawayAxisEnum.XAxis:
                    minBound = (center.X - halfExtents.X) + halfExtents.X;
                    maxBound = (center.X + halfExtents.X) + halfExtents.X;
                    break;

                case CutawayAxisEnum.YAxis:
                    minBound = center.Y - halfExtents.Y;
                    maxBound = center.Y + halfExtents.Y;
                    break;

                case CutawayAxisEnum.ZAxis:
                    minBound = center.Z - halfExtents.Z;
                    maxBound = center.Z + halfExtents.Z;
                    break;

                default:
                    throw new InvalidOperationException("Invalid axis");
            }

            cutawayPosition = (float)MathHelper.Clamp(cutawayPosition, minBound, maxBound);*/
        }

        private void DrawCutawayPlaneBillboard(PlaneD plane, Vector3D planePosition, IMyCubeGrid grid, CutawayAxisEnum currentAxis, Color color)
        {
            var min = grid.LocalAABB.Min / 2;
            var max = grid.LocalAABB.Max / 2;

            double planeWidth = 0;
            double planeHeight = 0;
            Vector3D normal = plane.Normal;
            Vector3D right = Vector3D.Zero;
            Vector3D up = Vector3D.Zero;

            switch (currentAxis)
            {
                case CutawayAxisEnum.XAxis:
                    planeWidth = Math.Abs(max.Y - min.Y);
                    planeHeight = Math.Abs(max.Z - min.Z);
                    right = grid.WorldMatrix.Up;
                    up = grid.WorldMatrix.Forward;
                    break;

                case CutawayAxisEnum.YAxis:
                    planeWidth = Math.Abs(max.X - min.X);
                    planeHeight = Math.Abs(max.Z - min.Z);
                    right = grid.WorldMatrix.Right;
                    up = grid.WorldMatrix.Forward;
                    break;

                case CutawayAxisEnum.ZAxis:
                    planeWidth = Math.Abs(max.X - min.X);
                    planeHeight = Math.Abs(max.Y - min.Y);
                    right = grid.WorldMatrix.Right;
                    up = grid.WorldMatrix.Up;
                    break;
            }

            Color planeColor = new Color(72, 119, 72, 50);
            Vector4 planeColorRef = planeColor.ToVector4();
            MyStringId planeTexture = MyStringId.GetOrCompute("CutawayPlane");
            MyTransparentGeometry.AddBillboardOriented(planeTexture, planeColorRef, planePosition, right, up, (float)planeWidth, (float)planeHeight, Vector2.Zero, BlendTypeEnum.Standard);
        }

        private double SignedDistanceToPoint(PlaneD plane, Vector3D point)
        {
            return Vector3D.Dot(plane.Normal, point) + plane.D;
        }

        private Vector3D GetOffsetVector(MatrixD orientation)
        {
            switch (CutawayAxis)
            {
                case CutawayAxisEnum.XAxis:
                    return orientation.Right * CutawayPosition;
                case CutawayAxisEnum.YAxis:
                    return orientation.Up * CutawayPosition;
                case CutawayAxisEnum.ZAxis:
                    return orientation.Forward * CutawayPosition;
                default:
                    return Vector3D.Zero;
            }
        }

        private Vector3D GetPlaneNormal(MatrixD orientation)
        {
            Vector3D normal;

            switch (CutawayAxis)
            {
                case CutawayAxisEnum.XAxis:
                    normal = orientation.Right;
                    break;
                case CutawayAxisEnum.YAxis:
                    normal = orientation.Up;
                    break;
                case CutawayAxisEnum.ZAxis:
                    normal = orientation.Forward;
                    break;
                default:
                    normal = Vector3D.Zero;
                    break;
            }

            return IsNormalInverted ? -normal : normal;
        }

        public void ClearCache(bool clearGrid = true, IMyCubeGrid grid = null)
        {
            foreach (var block in cachedGridBlocks)
            {
                IsVisible(block, true);
            }

            IsNormalInverted = false;
            CutawayPosition = 0 + 1.25f;

            if (grid != null || cachedGrid != null)
            {
                Vector3D gridCenter = grid != null ? grid.PositionComp.WorldAABB.Center : cachedGrid.PositionComp.WorldAABB.Center;
                MatrixD gridOrientation = grid != null ? grid.WorldMatrix : cachedGrid.WorldMatrix;
                Vector3D offsetVector = GetOffsetVector(gridOrientation);
                cutawayPlanePosition = gridCenter + offsetVector;
            }
            else
                cutawayPlanePosition = Vector3D.Zero;       

            StopDraw = true;

            if (clearGrid)
            {
                cachedGrid = null;
                cachedGridBlocks.Clear();
            }
        }

        private void IsVisible(IMySlimBlock block, bool visible)
        {
            if (block.FatBlock != null)
                block.FatBlock.Render.Visible = visible ? true : false ;
            else
                block.Dithering = visible ? 0 : -1;
        }
        #endregion
    }
}
