using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace glTFMilo.Source
{
    public enum MiloGame
    {
        RockBand3,
        TheBeatlesRockBand,
        RockBand2,
        RockBand1,
        GreenDayRockBand,
    }
    public struct GameRevisions
    {
        public static List<GameRevisions> revs = new List<GameRevisions>
        {
            new GameRevisions(
                MiloGame.RockBand3,
                rndDir: 10,
                model: 33,
                objDir: 27,
                trans: 9,
                draw: 3,
                tex: 11,
                bmp: 1,
                mat: 0x44,
                character: 17,
                anim: 4,
                group: 0xE,
                charTest: 15,
                milo: 28,
                light: 0x10
            ),
            new GameRevisions(
                MiloGame.TheBeatlesRockBand,
                rndDir: 10,
                model: 33,
                objDir: 22,
                trans: 9,
                draw: 3,
                tex: 10,
                bmp: 1,
                mat: 55,
                character: 15,
                anim: 4,
                group: 14,
                charTest: 10,
                milo: 25,
                light: 0xD
            ),
            new GameRevisions(
                MiloGame.RockBand2,
                rndDir: 10,
                model: 33,
                objDir: 20,
                trans: 9,
                draw: 3,
                tex: 10,
                bmp: 1,
                mat: 47,
                character: 12,
                anim: 4,
                group: 13,
                charTest: 8,
                milo: 25,
                light: 0xC
            )
        };

        public static GameRevisions GetRevision(MiloGame game)
        {
            return revs.FirstOrDefault(x => x.Game == game);
        }

        public MiloGame Game;
        public ushort RndDirRevision;
        public ushort ModelRevision;
        public ushort ObjectDirRevision;
        public ushort TransRevision;
        public ushort DrawableRevision;
        public ushort TextureRevision;
        public ushort BitmapRevision;
        public ushort MatRevision;
        public ushort CharacterRevision;
        public ushort AnimatableRevision;
        public ushort GroupRevision;
        public ushort CharacterTestingRevision;
        public ushort MiloRevision;
        public ushort LightRevision;

        public GameRevisions(
            MiloGame game,
            ushort rndDir,
            ushort model,
            ushort objDir,
            ushort trans,
            ushort draw,
            ushort tex,
            ushort bmp,
            ushort mat,
            ushort character,
            ushort anim,
            ushort group,
            ushort charTest,
            ushort milo,
            ushort light)
        {
            Game = game;
            RndDirRevision = rndDir;
            ModelRevision = model;
            ObjectDirRevision = objDir;
            TransRevision = trans;
            DrawableRevision = draw;
            TextureRevision = tex;
            BitmapRevision = bmp;
            MatRevision = mat;
            CharacterRevision = character;
            AnimatableRevision = anim;
            GroupRevision = group;
            CharacterTestingRevision = charTest;
            MiloRevision = milo;
            LightRevision = light;
        }
    }

}
