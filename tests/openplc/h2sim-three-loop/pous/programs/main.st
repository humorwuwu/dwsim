PROGRAM main
  VAR
    Temperature_Raw AT %IW0 : INT;
    Pressure_Raw AT %IW1 : INT;
    Level_Raw AT %IW2 : INT;
    Cooling_Raw AT %QW0 : INT;
    Backpressure_Raw AT %QW1 : INT;
    Level_Command_Raw AT %QW2 : INT;

    Temperature_SP : REAL := 80.0;
    Pressure_SP : REAL := 20.0;
    Level_SP : REAL := 50.0;
    Temperature_PV : REAL;
    Pressure_PV : REAL;
    Level_PV : REAL;
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

  (* The task interval is 100 ms. The three setpoint steps are staged for
     independent temperature, pressure and level loop runs. *)
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

  (* H2sim writes Int16 sentinels when the model is unhealthy. Reset the
     integrators in that state instead of acting on -32768. *)
  IF Temperature_Raw = -32768 THEN
    Temperature_PV := 0.0;
    Temperature_Integral := 0.0;
  ELSE
    Temperature_PV := INT_TO_REAL(Temperature_Raw) / 10.0;
  END_IF;

  IF Pressure_Raw = -32768 THEN
    Pressure_PV := 0.0;
    Pressure_Integral := 0.0;
  ELSE
    Pressure_PV := INT_TO_REAL(Pressure_Raw) / 100.0;
  END_IF;

  IF Level_Raw = -32768 THEN
    Level_PV := 0.0;
    Level_Integral := 0.0;
  ELSE
    Level_PV := INT_TO_REAL(Level_Raw) / 10.0;
  END_IF;

  (* PI loops use PV - SP so a positive command increases the measured
     variable. The output is clamped to 0-100 %; the integral is back-
     calculated to the clamped output for anti-windup. *)
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

  Cooling_Raw := REAL_TO_INT(Cooling_Output * 10.0);
  Backpressure_Raw := REAL_TO_INT(Backpressure_Output * 10.0);
  Level_Command_Raw := REAL_TO_INT(Level_Command * 10.0);
END_PROGRAM

CONFIGURATION Config0
  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := T#100ms, PRIORITY := 1);
    PROGRAM instance0 WITH task0 : main;
  END_RESOURCE
END_CONFIGURATION
