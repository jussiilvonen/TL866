﻿using System;
using System.IO;

namespace TL866
{
    public class Firmware
    {
        public const int UPDATE_DAT_SIZE = 312348;
        public const int BLOCK_SIZE = 0x50;
        public const int XOR_TABLE_SIZE = 0x100;
        public const int XOR_TABLE_START = 0x1EEDF;
        public const int XOR_TABLE_OFFSET = 0x1FC00;

        public const uint SERIAL_OFFSET = 0x1FD00;
        public const int FLASH_SIZE = 0x20000;
        public const int BOOTLOADER_SIZE = 0x1800;
        public const int ENCRYPTED_FIRMWARE_SIZE = 0x25D00;
        public const int UNENCRYPTED_FIRMWARE_SIZE = 0x1E400;
        public const int FIRMWARE_SIGNATURE_OFFSET = 0x1E3FC;

        public const uint A_BOOTLOADER_CRC = 0x95AB;
        public const uint CS_BOOTLOADER_CRC = 0x20D2;



        public const byte WRITE_COMMAND = 0xAA;
        public const byte ERASE_COMMAND = 0xCC;
        public const byte RESET_COMMAND = 0xFF;
        public const byte REPORT_COMMAND = 0x00;

        public const byte DUMPER_READ_FLASH = 0x01;
        public const byte DUMPER_WRITE_BOOTLOADER = 0x02;
        public const byte DUMPER_WRITE_CONFIG = 0x03;
        public const byte DUMPER_WRITE_INFO = 0x04;
        public const byte DUMPER_INFO = 0x05;

        public const uint TL866_VID = 0x04D8;
        public const uint TL866_PID = 0xE11C;

        public enum BOOTLOADER_TYPE { A_BOOTLOADER = 1, CS_BOOTLOADER = 2 }
        public enum FIRMWARE_TYPE   { FIRMWARE_A, FIRMWARE_CS, FIRMWARE_CUSTOM }
        public enum DEVICE_VERSION  { VERSION_TL866A = 1, VERSION_TL866CS = 2 }
        public enum DEVICE_STATUS   { NORMAL_MODE = 1, BOOTLOADER_MODE = 2 }
        public enum PROGRAMMER_TYPE { TL866A, TL866CS }
        public enum ENCRYPTION_KEY  { A_KEY, CS_KEY }



        private byte[] m_firmwareA;
        private byte[] m_firmwareCS;
        private byte m_eraseA;
        private byte m_eraseCS;
        private byte m_version;
        private bool m_IsValid = false;


        public void Open(string UpdateDat_Path)
        {
            m_IsValid = false;
            FileStream fsin = null;
            try
            {
                fsin = File.OpenRead(UpdateDat_Path);
            }
            catch
            {
                throw new Exception("Error opening file " + UpdateDat_Path);
            }
            if (fsin.Length != UPDATE_DAT_SIZE)
            {
                throw new Exception(UpdateDat_Path + "\nFile size error!");
            }
            byte[] inbuffer = new byte[fsin.Length + 1];
            byte[] outbuffer = new byte[ENCRYPTED_FIRMWARE_SIZE];
            fsin.Read(inbuffer, 0, (int)fsin.Length);

            m_eraseA = inbuffer[9];
            m_eraseCS = inbuffer[17];
            m_version = inbuffer[0];

            ////Decrypt A firmware (stage 1)
            int CryptoIndex = 0x14;
            uint FirmwareIndex = 0xA1C;
            uint idx1 = 0;
            uint idx2 = 0;
            for (uint i = 0; i <= outbuffer.Length - 1; i++)
            {
                idx1 = (uint)(CryptoIndex + 260 + ((BitConverter.ToInt32(inbuffer, CryptoIndex) + i) & 0x3ff));
                idx2 = (uint)(CryptoIndex + 4 + ((i / 80) & 0xff));
                outbuffer[i] = Convert.ToByte(inbuffer[FirmwareIndex + i] ^ (inbuffer[idx1] ^ inbuffer[idx2]));
            }
            crc32 crcc = new crc32();
            uint crc32 = BitConverter.ToUInt32(inbuffer, 4);
            uint crc = ~crcc.GetCRC32(outbuffer, 0xFFFFFFFF);
            if (crc != crc32)
            {
                throw new Exception(UpdateDat_Path + "\nData CRC error!");
            }
            m_firmwareA = new byte[outbuffer.Length];
            Array.Copy(outbuffer, m_firmwareA, outbuffer.Length);

            ////Decrypt CS firmware (stage 1)
            CryptoIndex = 0x518;
            FirmwareIndex = 0x2671C;
            for (uint i = 0; i <= outbuffer.Length - 1; i++)
            {
                idx1 = (uint)(CryptoIndex + 260 + ((BitConverter.ToInt32(inbuffer, CryptoIndex) + i) & 0x3FF));
                idx2 = (uint)(CryptoIndex + 4 + ((i / 80) & 0xFF));
                outbuffer[i] = Convert.ToByte((inbuffer[FirmwareIndex + i] ^ (inbuffer[idx1] ^ inbuffer[idx2])));
            }
            crc32 = BitConverter.ToUInt32(inbuffer, 12);
            crc = ~crcc.GetCRC32(outbuffer, 0xFFFFFFFF);
            if (crc != crc32)
            {
                throw (new Exception(UpdateDat_Path + "\nData CRC error!"));
            }
            m_firmwareCS = new byte[outbuffer.Length];
            Array.Copy(outbuffer, m_firmwareCS, outbuffer.Length);
            fsin.Close();
            byte[] b = new byte[123904];
            uint c1 = 0;
            uint c2 = 0;
            Decrypt_Firmware(b, (int)PROGRAMMER_TYPE.TL866A);
            c1 = BitConverter.ToUInt32(b, (int)FIRMWARE_SIGNATURE_OFFSET);
            Decrypt_Firmware(b, (int)PROGRAMMER_TYPE.TL866CS);
            c2 = BitConverter.ToUInt32(b, (int)FIRMWARE_SIGNATURE_OFFSET);
            if (c1 != 0x5AA5AA55 || c2 != 0x5AA5AA55)
            {
                throw new Exception("Firmware decryption error!");
            }
            m_IsValid = true;
        }


        public bool IsValid()
        {
            return m_IsValid;
        }


        public byte GetEraseParametter(int type)
        {
            return type == (int)PROGRAMMER_TYPE.TL866A ? m_eraseA : m_eraseCS;
        }

        public byte GetVersion()
        {
            return m_version;
        }



        public byte[] GetEncryptedFirmware(int type, int key)
        {
            byte[] buffer = new byte[ENCRYPTED_FIRMWARE_SIZE];
            if ((int)type == (int)key)
            {
                Array.Copy(type == (int)PROGRAMMER_TYPE.TL866A ? m_firmwareA : m_firmwareCS, buffer, buffer.Length);
                return buffer;
            }
            byte[] db = new byte[123904];
            Decrypt_Firmware(db, type);
            Array.Copy(Encrypt_Firmware(db, key), buffer, buffer.Length);
            return buffer;
        }

        public void Decrypt_Firmware(byte[] firmware, int type)
        {
            byte[] outbuffer = new byte[ENCRYPTED_FIRMWARE_SIZE];
            Array.Copy(type == (int)PROGRAMMER_TYPE.TL866A ? m_firmwareA : m_firmwareCS, outbuffer, outbuffer.Length);
            byte[] xortable = new byte[256];
            byte[] buffer = new byte[80];
            //extract encryption xor table
            for (uint i = 0; i <= outbuffer.Length - 1; i++)
            {
                outbuffer[i] = (byte)(outbuffer[i] ^ 255);
            }
            for (uint i = 0; i <= 15; i++)
            {
                Array.Copy(outbuffer, 0x1eedf + (i * 320), xortable, i * 16, 16);
            }
            //Restoring the buffer
            for (uint i = 0; i <= outbuffer.Length - 1; i++)
            {
                outbuffer[i] = (byte)(outbuffer[i] ^ 255);
            }
            //Decrypt each data block
            MemoryStream msout = new MemoryStream(firmware);
            UInt32 index = 0x15;
            for (uint i = 0; i <= outbuffer.Length - 1; i += 80)
            {
                Array.Copy(outbuffer, i, buffer, 0, 80);
                Decrypt_Block(buffer, xortable, index);
                msout.Write(buffer, 0, 64);
                index = (index + 4) & 0xff;
            }
            msout.Close();
        }

        public byte[] Encrypt_Firmware(byte[] firmware, int key)
        {
            byte[] outbuffer = new byte[ENCRYPTED_FIRMWARE_SIZE];
            Array.Copy(key == (int)ENCRYPTION_KEY.A_KEY ? m_firmwareA : m_firmwareCS, outbuffer, outbuffer.Length);
            byte[] xortable = new byte[256];
            byte[] buffer = new byte[80];
            //extract encryption xor table
            for (uint i = 0; i <= outbuffer.Length - 1; i++)
            {
                outbuffer[i] = (byte)(outbuffer[i] ^ 255);
            }
            for (uint i = 0; i <= 15; i++)
            {
                Array.Copy(outbuffer, 0x1EEDF + (i * 320), xortable, i * 16, 16);
            }
            //Encrypt each data block
            MemoryStream msout = new MemoryStream(outbuffer);
            UInt32 index = 0x15;
            for (uint i = 0; i <= firmware.Length - 1; i += 64)
            {
                Array.Copy(firmware, i, buffer, 0, 64);
                Encrypt_Block(buffer, xortable, index);
                msout.Write(buffer, 0, buffer.Length);
                index = (index + 4) & 0xff;
            }
            msout.Close();
            return outbuffer;
        }

        private void Encrypt_Block(byte[] data, byte[] xortable, uint index)
        {
            byte t = 0;
            for (int i = data.Length - 16; i <= data.Length - 1; i++)
            {
                data[i] = (byte)(Utils.Generator.Next(0, 255));
            }
            //step1: swap data bytes
            for (uint i = 0; i <= data.Length / 2 - 1; i += 4)
            {
                t = data[i];
                data[i] = data[data.Length - i - 1];
                data[data.Length - i - 1] = t;
            }
            //step2: shift the whole array three bits left
            for (uint i = 0; i <= data.Length - 2; i++)
            {
                data[i] = (byte)(((data[i] << 3) & 0xf8) | (data[i + 1] >> 5));
            }
            data[data.Length - 1] = (byte)((data[data.Length - 1] << 3) & 0xF8);
            //step3: xor
            for (uint i = 0; i <= data.Length - 1; i++)
            {
                data[i] = Convert.ToByte(data[i] ^ xortable[index]);
                index += 1;
                index = index & 0xFF;
            }
        }


        private void Decrypt_Block(byte[] data, byte[] xortable, uint index)
        {
            //step1: xor
            for (uint i = 0; i <= data.Length - 1; i++)
            {
                data[i] = Convert.ToByte(data[i] ^ xortable[index]);
                index += 1;
                index = index & 0xff;
            }
            //step2: shift the whole array three bits right
            for (int i = data.Length - 1; i >= 1; i += -1)
            {
                data[i] = (byte)((int)((data[i] >> 3) & 0x1F) | (data[i - 1] << 5));
            }
            data[0] = (byte)((int)((data[0] >> 3) & 0x1F));
            //step3: swap data bytes
            byte t = 0;
            for (uint i = 0; i <= data.Length / 2 - 1; i += 4)
            {
                t = data[i];
                data[i] = data[data.Length - i - 1];
                data[data.Length - i - 1] = t;
            }
        }



        public string[] GetSerialFromBin(byte[] FirmwareType)
        {
            byte[] key = new byte[80];
            Array.Copy(FirmwareType, 0x1fd00, key, 0, key.Length);
            DecryptSerial(key, FirmwareType);
            return (new string[] { System.Text.Encoding.UTF8.GetString(key, 0, 8), System.Text.Encoding.UTF8.GetString(key, 8, 24) });
        }

        public void DecryptSerial(byte[] key, byte[] firmware)
        {
            //step1
            uint index = 0xA;
            for (uint i = 0; i <= key.Length - 1; i++)
            {
                key[i] = (byte)(key[i] ^ firmware[0x1fc00 + index]);
                index += 1;
                index = index & 0xff;
            }
            //step2
            for (int i = key.Length - 1; i >= 1; i += -1)
            {
                key[i] = (byte)(((key[i] >> 3) & 0x1f) | (key[i - 1] << 5));
            }
            key[0] = (byte)((key[0] >> 3) & 0x1f);
            //step3
            byte t = 0;
            for (int i = 0; i <= key.Length / 2 - 1; i += 4)
            {
                t = key[i];
                key[i] = key[key.Length - i - 1];
                key[key.Length - i - 1] = t;
            }
        }


        public ushort GetKeyCRC(byte[] key)
        {
            Crc16 crcc = new Crc16();
            return crcc.GetCRC16(key, 0);
        }


        private void Make_CRC(byte[] key)
        {
            byte[] b = new byte[key.Length - 2];
            ushort crc = 0;
            Array.Copy(key, 0, b, 0, b.Length);
            do
            {
                for (int i = 32; i <= b.Length - 1; i++)
                {
                    b[i] = (byte)Utils.Generator.Next(0, 255);
                }
                crc = GetKeyCRC(b);
            } while (!((crc < 0x2000)));
            Array.Copy(b, 0, key, 0, b.Length);
            key[key.Length - 1] = Convert.ToByte((crc >> 8));
            key[key.Length - 2] = Convert.ToByte(crc & 0xff);
        }

        public void EncryptSerial(byte[] key, byte[] firmware)
        {
            uint index = 0xa;
            byte t = 0;
            if (GetKeyCRC(key) != 0)
                Make_CRC(key);
            //step1
            for (int i = 0; i <= key.Length / 2 - 1; i += 4)
            {
                t = key[i];
                key[i] = key[key.Length - i - 1];
                key[key.Length - i - 1] = t;
            }
            //step2
            for (int i = 0; i <= key.Length - 2; i++)
            {
                key[i] = (byte)(((key[i] << 3) & 0xf8) | (key[i + 1] >> 5));
            }
            key[key.Length - 1] = (byte)((key[key.Length - 1] << 3) & 0xf8);
            //step3
            for (int i = 0; i <= key.Length - 1; i++)
            {
                key[i] = (byte)(key[i] ^ firmware[0x1fc00 + index]);
                index += 1;
                index = index & 0xff;
            }
        }

    }
}