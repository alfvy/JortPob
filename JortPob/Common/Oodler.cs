using System.IO;

namespace JortPob.Common
{
    public static class Oodler
    {
        /* Copies oo2core from Elden Rings files to JortPobs runtime directory. oo2core is copyrighted so we cannot redistribute it legally. this circumvents that */
        public static void Initialize()
        {
            string oodlePath = Path.Combine(System.AppContext.BaseDirectory, "oo2core_6_win64.dll");
            if(!File.Exists(oodlePath))
            {
                string grabPath = Path.Combine(Const.ELDEN_PATH, @"game\oo2core_6_win64.dll");
                File.Copy(grabPath, oodlePath);
            }
        }
        
    }
}
