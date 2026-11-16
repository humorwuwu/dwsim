PROGRAM main
  VAR
    Temperature_SP : REAL := 80.0;
    Pressure_SP : REAL := 20.0;
    Level_SP : REAL := 50.0;
    Temperature_PV : REAL := 80.0;
    Pressure_PV : REAL := 20.0;
    Level_PV : REAL := 50.0;
    Temperature_Error : REAL;
    Pressure_Error : REAL;
    Level_Error : REAL;
    Temperature_Integral : REAL;
    Pressure_Integral : REAL;
    Level_Integral : REAL;
    Cooling_Output : REAL;
    Backpressure_Output : REAL;
    Level_Command : REAL;
    Cycle_Count : DINT := 0;
    Setpoint_Stage : INT := 0;
  END_VAR

  (* The OPC UA server exposes the setpoints, measured values and commands.
     H2sim is the OPC UA client: it writes the three PVs and reads commands. *)
  Cycle_Count := Cycle_Count + 1;

  IF Cycle_Count >= 600 AND Setpoint_Stage < 1 THEN
    Temperature_SP := 85.0;
    Setpoint_Stage := 1;
  END_IF;

  IF Cycle_Count >= 1200 AND Setpoint_Stage < 2 THEN
    Pressure_SP := 30.0;
    Setpoint_Stage := 2;
  END_IF;

  IF Cycle_Count >= 1800 AND Setpoint_Stage < 3 THEN
    Level_SP := 70.0;
    Setpoint_Stage := 3;
  END_IF;

  Temperature_Error := Temperature_PV - Temperature_SP;
  Temperature_Integral := Temperature_Integral + 10.0 * Temperature_Error * 0.1;
  Cooling_Output := 80.0 * Temperature_Error + Temperature_Integral;
  IF Cooling_Output > 100.0 THEN
    Cooling_Output := 100.0;
  ELSIF Cooling_Output < 0.0 THEN
    Cooling_Output := 0.0;
  END_IF;
  Temperature_Integral := Cooling_Output - 80.0 * Temperature_Error;

  Pressure_Error := Pressure_PV - Pressure_SP;
  Pressure_Integral := Pressure_Integral + 2.0 * Pressure_Error * 0.1;
  Backpressure_Output := 30.0 * Pressure_Error + Pressure_Integral + 50.0;
  IF Backpressure_Output > 100.0 THEN
    Backpressure_Output := 100.0;
  ELSIF Backpressure_Output < 0.0 THEN
    Backpressure_Output := 0.0;
  END_IF;
  Pressure_Integral := Backpressure_Output - 50.0 - 30.0 * Pressure_Error;

  Level_Error := Level_PV - Level_SP;
  Level_Integral := Level_Integral + 2.0 * Level_Error * 0.1;
  Level_Command := 50.0 * Level_Error + Level_Integral + 50.0;
  IF Level_Command > 100.0 THEN
    Level_Command := 100.0;
  ELSIF Level_Command < 0.0 THEN
    Level_Command := 0.0;
  END_IF;
  Level_Integral := Level_Command - 50.0 - 50.0 * Level_Error;
END_PROGRAM

CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#100ms, PRIORITY := 1);
    PROGRAM instance0 WITH task0 : main;
  END_RESOURCE
END_CONFIGURATION
