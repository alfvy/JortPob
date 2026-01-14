using SoulsFormats;

namespace PortJob
{
    /* Class that takes an MSB and auto generates the resource list for it */
    /* This is technically less efficent than just adding them normally but it takes a lot of repetitive coding out of portjob so we doing this */
    /* Also I'm sure there is a way to do this with generics but i cannot be fucking asked to code that right now. it's 5am and i have beers to drink biiiiitch */
    class AutoResource
    {
        public static void Generate(int map, int x, int y, int block, MSBE msb)
        {
            /* Player */
            MSBE.Model.Player playerRes = new();
            playerRes.Name = "c0000";
            playerRes.SibPath = @"N:\FDP\data\Model\chr\c0000\sib\c0000.SIB";
            msb.Models.Players.Add(playerRes);

            /* Assets */
            foreach (MSBE.Part.Asset ass in msb.Parts.Assets)
            {
                bool exists = false;
                foreach (MSBE.Model.Asset res in msb.Models.Assets)
                {
                    if (ass.ModelName == res.Name) { exists = true; }
                }
                if (exists) { continue; }

                MSBE.Model.Asset nures = new();
                nures.Name = ass.ModelName;
                nures.SibPath = @$"N:\GR\data\Asset\Environment\geometry\{ass.ModelName.Substring(7)}\{ass.ModelName}\sib\{ass.ModelName}.sib";
                msb.Models.Assets.Add(nures);
            }

            /* Map Pieces */
            foreach (MSBE.Part.MapPiece mp in msb.Parts.MapPieces)
            {
                bool exists = false;
                foreach (MSBE.Model.MapPiece res in msb.Models.MapPieces)
                {
                    if (mp.ModelName == res.Name) { exists = true; }
                }
                if (exists) { continue; }

                MSBE.Model.MapPiece nures = new();
                nures.Name = mp.ModelName;
                nures.SibPath = @$"N:\FDP\data\Model\map\m{map:D2}_{x:D2}_{y:D2}_{block:D2}\sib\{mp.ModelName}.sib";
                msb.Models.MapPieces.Add(nures);
            }

            /* Collisions */
            foreach (MSBE.Part.Collision col in msb.Parts.Collisions)
            {
                bool exists = false;
                foreach (MSBE.Model.Collision res in msb.Models.Collisions)
                {
                    if (col.ModelName == res.Name) { exists = true; }
                }
                if (exists) { continue; }

                MSBE.Model.Collision nures = new();
                nures.Name = col.ModelName;
                nures.SibPath = @$"N:\FDP\data\Model\map\m{map:D2}_{x:D2}_{y:D2}_{block:D2}\hkt\{col.ModelName}.hkt";
                msb.Models.Collisions.Add(nures);
            }

            /* Connect Collision */
            foreach (MSBE.Part.ConnectCollision con in msb.Parts.ConnectCollisions)
            {
                bool exists = false;
                foreach (MSBE.Model.Collision res in msb.Models.Collisions)
                {
                    if (con.ModelName == res.Name) { exists = true; }
                }
                if (exists) { continue; }

                MSBE.Model.Collision nures = new();
                nures.Name = con.ModelName;
                nures.SibPath = @$"N:\FDP\data\Model\map\m{map:D2}_{x:D2}_{y:D2}_{block:D2}\hkt\{con.ModelName}.hkt";
                msb.Models.Collisions.Add(nures);
            }

            /* Enemy */
            foreach (MSBE.Part.Enemy ene in msb.Parts.Enemies)
            {
                bool exists = false;
                foreach (MSBE.Model.Enemy res in msb.Models.Enemies)
                {
                    if (ene.ModelName == res.Name) { exists = true; }
                }
                if (exists) { continue; }

                MSBE.Model.Enemy nures = new();
                nures.Name = ene.ModelName;
                nures.SibPath = "";
                msb.Models.Enemies.Add(nures);
            }
        }
    }
}