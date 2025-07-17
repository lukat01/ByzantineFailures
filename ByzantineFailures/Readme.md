# Byzantine Generals Simulation Project

This is a simple simulation of the Byzantine Generals problem, demonstrating how to handle communication failures in a 
distributed system. This project simulates a scenario where generals sign messages that they send.

## Features

- Basic simulation with variable number of lieutenants
- Full logging (both in console and in files)
- Checkpoints for simulation of system failure
- Execution of the program from previously stored checkpoint

## Getting Started

1. After cloning the repository open the .sln file in Visual Studio.
1. The project uses .NET 9.0
1. Edit the launchSettings.json file to configure the run
1. Run the project

## Different Run Options

- **Run from start**: In launchSettings.json, commandLineArgs should contain 3 integers that represent: 
  - `Number Of Generals`: Total number of generals in the simulation.
  - `Number Of Unloyal Generals`: Number of unloyal generals.
  - `Indicator if the chief general is loyal`: 1 for loyal, 0 for unloyal.

- **Run from checkpoint**: In launchSettings.json, commandLineArgs should contain file name of the checkpoint to load. 
The file should be in the `checkpoints` folder.

## Checkpoints

If you want to save the state of the simulation, you can press Ctrl-C when communication starts. 
This will create a checkpoint file in the `checkpoints` folder.
