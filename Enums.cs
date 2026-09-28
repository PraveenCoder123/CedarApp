namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    public class Enums
    {
        public enum LaserWriteCommand
        {
            Connect,
            Disconnect,
            Initialize,

            TurnOn,
            TurnOff,
            EnableTemperature,
            EnablePower,
            EnablePiezo,
            EnableFeedforward,

            DisableTemperature,
            DisablePower,
            DisablePiezo,
            DisableFeedforward,

            SetWavelength,
            SetPower,
            SetTemperature,
            SetCurrent,
            SetPiezo,
            SetFeedforward,
            SetPowerStabilization
        }

        public enum LaserReadProperty
        {
            Connected,
            Disconnected,
            Status,
            Enabled,
            DeviceHealth,
            DeviceBusy,
            LaserEmissionStatus,

            TemperatureActual,
            TemperatureReady,
            TemperatureSetpoint,
            TemperatureEnabled,
            TemperatureFault,

            WavelengthActual,
            WavelengthSetpoint,
            WavelengthMinimum,
            WavelengthMaximum,

            PowerSetpoint,
            PowerMinimum,
            PowerMaximum,
            PowerStabilization,
            PowerStabilizationEnabled,
            PowerState,
            FeedforwardEnabled,

            PiezoEnabled,
            PiezoVoltageActual,
            PiezoVoltageMinimum,
            PiezoVoltageMaximum,

            CtlStateText,
            CtlState,
            MotorStatusText,
            MotorStatus,
            CtlPower,
            

            CtlModel,
            DeviceLabel,
            SerialNumber,
            FirmwareVersion,
            HardwareVersion,
            Manufacturer,
            ProductName,

            IsOn,
            IsInitialized,
            IsBusy,
            IsReady,
            IsError,
            ErrorCode,
            ErrorMessage,

            LaserCurrentActual,
            LaserCurrentSetpoint,
            LaserCurrentLimit,
           
        }

        public enum LaserType
        {
            TunableToptica,
            FixedLabView,
            TunableThorlabs
        }
    }
}
