namespace MiSTerCast
{
    static class ModelineValidator
    {
        public static bool TryValidate(Modeline modeline, out string error)
        {
            if (modeline.pclock <= 0)
            {
                error = "Modeline pixel clock must be greater than 0.";
                return false;
            }

            if (modeline.hactive == 0 || modeline.vactive == 0)
            {
                error = "Modeline active width and height must be greater than 0.";
                return false;
            }

            if (modeline.hactive > modeline.hbegin ||
                modeline.hbegin > modeline.hend ||
                modeline.hend > modeline.htotal ||
                modeline.htotal <= modeline.hactive)
            {
                error = "Horizontal timing must satisfy Hactive <= Hbegin <= Hend <= Htotal, with Htotal greater than Hactive.";
                return false;
            }

            if (modeline.vactive > modeline.vbegin ||
                modeline.vbegin > modeline.vend ||
                modeline.vend > modeline.vtotal ||
                modeline.vtotal <= modeline.vactive)
            {
                error = "Vertical timing must satisfy Vactive <= Vbegin <= Vend <= Vtotal, with Vtotal greater than Vactive.";
                return false;
            }

            error = "";
            return true;
        }
    }
}
