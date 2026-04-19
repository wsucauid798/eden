/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

// Constants previously exposed to LSL scripts for addressing BulletSim physics
// extensions. The ll*-binding machinery was demolished with the LSL frontend;
// the constants themselves are still consumed by BulletS internals (BSPrim,
// BSPrimLinkable, BSLinksetConstraints) via switch-case dispatch, so they stay.

namespace OpenSim.Region.PhysicsModule.BulletS
{
    public static class ExtendedPhysics
    {
        // Per-prim function identifiers.
        public const string PhysFunctGetLinksetType = "BulletSim.GetLinksetType";
        public const string PhysFunctSetLinksetType = "BulletSim.SetLinksetType";
        public const string PhysFunctChangeLinkFixed = "BulletSim.ChangeLinkFixed";
        public const string PhysFunctChangeLinkType = "BulletSim.ChangeLinkType";
        public const string PhysFunctGetLinkType = "BulletSim.GetLinkType";
        public const string PhysFunctChangeLinkParams = "BulletSim.ChangeLinkParams";
        public const string PhysFunctAxisLockLimits = "BulletSim.AxisLockLimits";
        public const string PhysFunctDisableDeactivation = "BulletSim.DisableDeactivation";

        public const int PHYS_CENTER_OF_MASS = 1 << 0;

        // Axis-lock / limit selectors.
        public const int PHYS_AXIS_LOCK_LINEAR     = 14700;
        public const int PHYS_AXIS_LOCK_LINEAR_X   = 14701;
        public const int PHYS_AXIS_LIMIT_LINEAR_X  = 14702;
        public const int PHYS_AXIS_LOCK_LINEAR_Y   = 14703;
        public const int PHYS_AXIS_LIMIT_LINEAR_Y  = 14704;
        public const int PHYS_AXIS_LOCK_LINEAR_Z   = 14705;
        public const int PHYS_AXIS_LIMIT_LINEAR_Z  = 14706;
        public const int PHYS_AXIS_LOCK_ANGULAR    = 14707;
        public const int PHYS_AXIS_LOCK_ANGULAR_X  = 14708;
        public const int PHYS_AXIS_LIMIT_ANGULAR_X = 14709;
        public const int PHYS_AXIS_LOCK_ANGULAR_Y  = 14710;
        public const int PHYS_AXIS_LIMIT_ANGULAR_Y = 14711;
        public const int PHYS_AXIS_LOCK_ANGULAR_Z  = 14712;
        public const int PHYS_AXIS_LIMIT_ANGULAR_Z = 14713;
        public const int PHYS_AXIS_UNLOCK_LINEAR   = 14714;
        public const int PHYS_AXIS_UNLOCK_LINEAR_X = 14715;
        public const int PHYS_AXIS_UNLOCK_LINEAR_Y = 14716;
        public const int PHYS_AXIS_UNLOCK_LINEAR_Z = 14717;
        public const int PHYS_AXIS_UNLOCK_ANGULAR  = 14718;
        public const int PHYS_AXIS_UNLOCK_ANGULAR_X = 14719;
        public const int PHYS_AXIS_UNLOCK_ANGULAR_Y = 14720;
        public const int PHYS_AXIS_UNLOCK_ANGULAR_Z = 14721;
        public const int PHYS_AXIS_UNLOCK           = 14722;

        // Linkset types.
        public const int PHYS_LINKSET_TYPE_CONSTRAINT  = 0;
        public const int PHYS_LINKSET_TYPE_COMPOUND    = 1;
        public const int PHYS_LINKSET_TYPE_MANUAL      = 2;

        // Link constraint types.
        public const int PHYS_LINK_TYPE_FIXED  = 1234;
        public const int PHYS_LINK_TYPE_HINGE  = 4;
        public const int PHYS_LINK_TYPE_SPRING = 9;
        public const int PHYS_LINK_TYPE_6DOF   = 6;
        public const int PHYS_LINK_TYPE_SLIDER = 7;

        // Link parameters.
        public const int PHYS_PARAM_MIN                       = 14401;
        public const int PHYS_PARAM_FRAMEINA_LOC              = 14401;
        public const int PHYS_PARAM_FRAMEINA_ROT              = 14402;
        public const int PHYS_PARAM_FRAMEINB_LOC              = 14403;
        public const int PHYS_PARAM_FRAMEINB_ROT              = 14404;
        public const int PHYS_PARAM_LINEAR_LIMIT_LOW          = 14405;
        public const int PHYS_PARAM_LINEAR_LIMIT_HIGH         = 14406;
        public const int PHYS_PARAM_ANGULAR_LIMIT_LOW         = 14407;
        public const int PHYS_PARAM_ANGULAR_LIMIT_HIGH        = 14408;
        public const int PHYS_PARAM_USE_FRAME_OFFSET          = 14409;
        public const int PHYS_PARAM_ENABLE_TRANSMOTOR         = 14410;
        public const int PHYS_PARAM_TRANSMOTOR_MAXVEL         = 14411;
        public const int PHYS_PARAM_TRANSMOTOR_MAXFORCE       = 14412;
        public const int PHYS_PARAM_CFM                       = 14413;
        public const int PHYS_PARAM_ERP                       = 14414;
        public const int PHYS_PARAM_SOLVER_ITERATIONS         = 14415;
        public const int PHYS_PARAM_SPRING_AXIS_ENABLE        = 14416;
        public const int PHYS_PARAM_SPRING_DAMPING            = 14417;
        public const int PHYS_PARAM_SPRING_STIFFNESS          = 14418;
        public const int PHYS_PARAM_LINK_TYPE                 = 14419;
        public const int PHYS_PARAM_USE_LINEAR_FRAMEA         = 14420;
        public const int PHYS_PARAM_SPRING_EQUILIBRIUM_POINT  = 14421;
        public const int PHYS_PARAM_MAX                       = 14421;

        // Axis identifiers.
        public const int PHYS_AXIS_ALL         = -1;
        public const int PHYS_AXIS_LINEAR_ALL  = -2;
        public const int PHYS_AXIS_ANGULAR_ALL = -3;
        public const int PHYS_AXIS_LINEAR_X    = 0;
        public const int PHYS_AXIS_LINEAR_Y    = 1;
        public const int PHYS_AXIS_LINEAR_Z    = 2;
        public const int PHYS_AXIS_ANGULAR_X   = 3;
        public const int PHYS_AXIS_ANGULAR_Y   = 4;
        public const int PHYS_AXIS_ANGULAR_Z   = 5;
    }
}
