using System;
using System.IO.Ports;

public class SerialBootloader
{
    private SerialPort serialPort;

    // Construtor para abrir a porta serial
    public SerialBootloader(string portName, int baudRate)
    {
        serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        serialPort.Open();
    }

    // Método para enviar dados pela serial
    public void WriteData(byte[] data)
    {
        serialPort.Write(data, 0, data.Length);
        Console.WriteLine("Dados enviados!");
    }

    // Método para receber dados da serial
    public byte[] ReadData(int size)
    {
        byte[] buffer = new byte[size];
        serialPort.Read(buffer, 0, size);
        return buffer;
    }

    // Método para fechar a conexão serial
    public void Close()
    {
        serialPort.Close();
    }
}
