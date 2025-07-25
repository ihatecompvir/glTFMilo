using glTFMilo.Source;
using MiloGLTFUtils.Source.Shared;
using MiloLib.Assets;
using MiloLib.Assets.Char;
using MiloLib.Assets.P9;
using MiloLib.Assets.Rnd;
using MiloLib.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MiloGLTFUtils.Source.glTFMilo.Core
{
    public static class DirBuilder
    {
        public static void BuildCharacterDirectory(Options opts, MiloGame selectedGame, DirectoryMeta meta)
        {
            Character character = Character.New(GameRevisions.GetRevision(selectedGame).CharacterRevision, 0);
            character.currentViewportIdx = 6;
            character.objFields.revision = 2;
            character.inlineProxy = opts.Type == "instrument";
            character.charTest = Character.CharacterTesting.New(GameRevisions.GetRevision(selectedGame).CharacterTestingRevision, 0);
            character.charTest.distMap = "none";

            if (opts.Type == "character" && selectedGame == MiloGame.RockBand3)
                character.subDirs.Add("../../shared/char_shared.milo");

            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            character.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });

            character.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
            character.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
            character.draw.sphere.radius = 10000.0f;
            character.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);
            character.sphereBase = meta.name;

            typeof(RndDir).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, GameRevisions.GetRevision(selectedGame).RndDirRevision);
            typeof(ObjectDir).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(character, GameRevisions.GetRevision(selectedGame).ObjectDirRevision);

            meta.directory = character;
        }

        public static void BuildRndDirectory(Options opts, MiloGame selectedGame, DirectoryMeta meta)
        {
            RndDir rndDir = new RndDir(0, 0);
            rndDir.currentViewportIdx = 6;
            rndDir.objFields.revision = 2;
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.viewports.Add(new Matrix() { m11 = 1.0f, m12 = 0.0f, m13 = 0.0f, m21 = 0.0f, m22 = 1.0f, m23 = 0.0f, m31 = 0.0f, m32 = 0.0f, m33 = 1.0f, m41 = 0.0f, m42 = 0.0f, m43 = 0.0f });
            rndDir.anim = RndAnimatable.New(GameRevisions.GetRevision(selectedGame).AnimatableRevision, 0);
            rndDir.draw = RndDrawable.New(GameRevisions.GetRevision(selectedGame).DrawableRevision, 0);
            rndDir.draw.sphere.radius = 10000.0f;
            rndDir.trans = RndTrans.New(GameRevisions.GetRevision(selectedGame).TransRevision, 0);

            typeof(RndDir).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(rndDir, GameRevisions.GetRevision(selectedGame).RndDirRevision);
            typeof(ObjectDir).GetField("revision", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(rndDir, GameRevisions.GetRevision(selectedGame).ObjectDirRevision);

            meta.directory = rndDir;
        }
    }
}
