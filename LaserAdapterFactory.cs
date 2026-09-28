using ControlSystem;
using TunableLaserDriver;

namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    /// <summary>
    /// Builds concrete <see cref="ILaserAdapter"/> instances, injecting
    /// their dependencies (driver, IO module). Keeps configuration and
    /// singleton lookups out of the adapters themselves so they remain
    /// unit-testable.
    /// </summary>
    internal static class LaserAdapterFactory
    {
        /// <summary>
        /// Creates a Toptica tunable-laser adapter using IP/port values
        /// from <see cref="AppSettingsManager"/> and a real
        /// <see cref="TunableLaserDriver.TunableLaserDriver"/> instance.
        /// </summary>
        /// <returns>A configured <see cref="TunableTopticaLaserService"/>.</returns>
        public static ILaserAdapter CreateToptica()
        {
            //var ipAddress ="127.0.0.1";
            //var port = 1998;
            var ipAddress = AppSettingsManager.SingleInstance.GetStringValue(
                StringConstants.AppSettingKeys.TunableLaserSettings_IpAddress);
            var port = AppSettingsManager.SingleInstance.GetIntValue(
                StringConstants.AppSettingKeys.TunableLaserSettings_Port);

            ITunableLaserDriver driver =
                new TunableLaserDriver.TunableLaserDriver(
                    ipAddress,
                    port,
                    LogManager.SingleInstance);

            return new TunableTopticaLaserService(driver);
        }

        /// <summary>
        /// Creates a fixed-wavelength LabVIEW laser adapter, injecting the
        /// application-wide <see cref="DigitalIOModule"/> singleton so the
        /// adapter shares the same LabVIEW interface as the rest of the app.
        /// </summary>
        /// <returns>A configured <see cref="FixedLabviewLaserService"/>.</returns>
        public static ILaserAdapter CreateFixedLabview()
        {
            DigitalIOModule digitalIO =
                HostInterface.SingleInstance.DigiatalInOutModulePointer;

            return new FixedLabviewLaserService(digitalIO);
        }
    }
}