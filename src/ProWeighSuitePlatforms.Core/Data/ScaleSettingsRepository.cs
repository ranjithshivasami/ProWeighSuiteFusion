using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Models;
using System.Collections.Generic;
using System;

namespace ProWeighSuitePlatforms.Data
{
    public class ScaleSettingsRepository
    {
        private readonly string _dbPath;
        public ScaleSettingsRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        private SqliteConnection GetConnection()
            => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS scale_settings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                scale_name TEXT NOT NULL DEFAULT '',
                connection_type TEXT NOT NULL DEFAULT 'SerialPort',
                port_name TEXT NOT NULL DEFAULT '',
                baud_rate INTEGER NOT NULL DEFAULT 9600,
                data_bits INTEGER NOT NULL DEFAULT 8,
                parity INTEGER NOT NULL DEFAULT 0,
                stop_bits INTEGER NOT NULL DEFAULT 1,
                ip_address TEXT NOT NULL DEFAULT '',
                port INTEGER NOT NULL DEFAULT 5000,
                mode TEXT NOT NULL DEFAULT 'Continuous',
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                scale_code INTEGER NOT NULL DEFAULT 0,
                location TEXT NOT NULL DEFAULT '',
                is_auto_capture INTEGER NOT NULL DEFAULT 0,
                capture_interval_ms INTEGER NOT NULL DEFAULT 500,
                target_uom TEXT NOT NULL DEFAULT 'count',
                sample_scale_data TEXT NOT NULL DEFAULT '',
                delimiter TEXT NOT NULL DEFAULT ''
            );
            ";
            await cmd.ExecuteNonQueryAsync();

            // Migration: Add sample_scale_data column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='sample_scale_data';
            ";
            var sampleScaleDataExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!sampleScaleDataExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN sample_scale_data TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add delimiter column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='delimiter';
            ";
            var delimiterExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!delimiterExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN delimiter TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add scale_name column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='scale_name';
            ";
            var columnExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!columnExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN scale_name TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add connection_type column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='connection_type';
            ";
            var connectionTypeExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!connectionTypeExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN connection_type TEXT NOT NULL DEFAULT 'SerialPort';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add ip_address column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='ip_address';
            ";
            var ipAddressExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!ipAddressExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN ip_address TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add port column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='port';
            ";
            var portExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!portExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN port INTEGER NOT NULL DEFAULT 5000;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add mode column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='mode';
            ";
            var modeExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!modeExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN mode TEXT NOT NULL DEFAULT 'Continuous';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add is_active column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='is_active';
            ";
            var isActiveExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!isActiveExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add zero_return column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='zero_return';
            ";
            var zeroReturnExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!zeroReturnExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN zero_return INTEGER NOT NULL DEFAULT 0;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add created_at column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='created_at';
            ";
            var createdAtExists = (long?)await cmd.ExecuteScalarAsync() > 0;
            
            if (!createdAtExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN created_at TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
                
                // Update existing records with current timestamp
                cmd.CommandText = @"
                UPDATE scale_settings 
                SET created_at = datetime('now') 
                WHERE created_at = '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add scale_code column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='scale_code';
            ";
            var scaleCodeExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!scaleCodeExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN scale_code INTEGER NOT NULL DEFAULT 0;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add location column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='location';
            ";
            var locationExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!locationExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN location TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add is_auto_capture column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='is_auto_capture';
            ";
            var isAutoCaptureExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!isAutoCaptureExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN is_auto_capture INTEGER NOT NULL DEFAULT 0;
                ";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN capture_interval_ms INTEGER NOT NULL DEFAULT 500;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add target_uom column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('scale_settings') 
            WHERE name='target_uom';
            ";
            var targetUomExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!targetUomExists)
            {
                cmd.CommandText = @"
                ALTER TABLE scale_settings 
                ADD COLUMN target_uom TEXT NOT NULL DEFAULT 'count';
                ";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<ScaleSettings>> GetAllAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, scale_name, connection_type, port_name, baud_rate, data_bits, parity, stop_bits, 
                       ip_address, port, mode, is_active, created_at, COALESCE(zero_return, 0) as zero_return,
                       COALESCE(scale_code, 0) as scale_code, COALESCE(location, '') as location,
                       COALESCE(is_auto_capture, 0) as is_auto_capture, COALESCE(capture_interval_ms, 500) as capture_interval_ms,
                       COALESCE(sample_scale_data, '') as sample_scale_data, COALESCE(delimiter, '') as delimiter
                FROM scale_settings 
                ORDER BY scale_name";
            
            var scales = new List<ScaleSettings>();
            using var reader = await cmd.ExecuteReaderAsync();
            int serialNumber = 1;
            while (await reader.ReadAsync())
            {
                scales.Add(new ScaleSettings
                {
                    Id = reader.GetInt32(0),
                    ScaleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ConnectionType = reader.IsDBNull(2) ? "SerialPort" : reader.GetString(2),
                    PortName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    BaudRate = reader.GetInt32(4),
                    DataBits = reader.GetInt32(5),
                    Parity = reader.GetInt32(6),
                    StopBits = reader.GetInt32(7),
                    IpAddress = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    Port = reader.GetInt32(9),
                    Mode = reader.IsDBNull(10) ? "Continuous" : reader.GetString(10),
                    IsActive = reader.GetInt32(11) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(12)),
                    ZeroReturn = reader.GetInt32(13) == 1,
                    ScaleCode = reader.GetInt32(14),
                    Location = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    IsAutoCapture = reader.GetInt32(16) == 1,
                    CaptureIntervalMs = reader.GetInt32(17),
                    SampleScaleData = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    Delimiter = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                    SerialNumber = serialNumber++
                });
            }
            return scales;
        }

        public async Task<ScaleSettings?> GetAsync()
        {
            return await GetByIdAsync(1);
        }

        public async Task<ScaleSettings?> GetByIdAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, scale_name, connection_type, port_name, baud_rate, data_bits, parity, stop_bits, 
                       ip_address, port, mode, is_active, created_at, COALESCE(zero_return, 0) as zero_return,
                       COALESCE(scale_code, 0) as scale_code, COALESCE(location, '') as location,
                       COALESCE(is_auto_capture, 0) as is_auto_capture, COALESCE(capture_interval_ms, 500) as capture_interval_ms,
                       COALESCE(sample_scale_data, '') as sample_scale_data, COALESCE(delimiter, '') as delimiter
                FROM scale_settings 
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ScaleSettings
                {
                    Id = reader.GetInt32(0),
                    ScaleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ConnectionType = reader.IsDBNull(2) ? "SerialPort" : reader.GetString(2),
                    PortName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    BaudRate = reader.GetInt32(4),
                    DataBits = reader.GetInt32(5),
                    Parity = reader.GetInt32(6),
                    StopBits = reader.GetInt32(7),
                    IpAddress = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    Port = reader.GetInt32(9),
                    Mode = reader.IsDBNull(10) ? "Continuous" : reader.GetString(10),
                    IsActive = reader.GetInt32(11) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(12)),
                    ZeroReturn = reader.GetInt32(13) == 1,
                    ScaleCode = reader.GetInt32(14),
                    Location = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    IsAutoCapture = reader.GetInt32(16) == 1,
                    CaptureIntervalMs = reader.GetInt32(17),
                    SampleScaleData = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    Delimiter = reader.IsDBNull(19) ? string.Empty : reader.GetString(19)
                };
            }
            return null;
        }

        public async Task<int> AddAsync(ScaleSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO scale_settings (scale_name, connection_type, port_name, baud_rate, data_bits, parity, stop_bits, 
                                        ip_address, port, mode, is_active, created_at, zero_return, scale_code, location,
                                        is_auto_capture, capture_interval_ms, sample_scale_data, delimiter)
            VALUES ($scaleName, $connectionType, $port, $baud, $data, $parity, $stop, $ipAddress, $tcpPort, $mode, $isActive, $createdAt, $zeroReturn, $scaleCode, $location, $isAutoCapture, $captureIntervalMs, $sampleScaleData, $delimiter);
            SELECT last_insert_rowid();";
            
            cmd.Parameters.AddWithValue("$scaleName", settings.ScaleName);
            cmd.Parameters.AddWithValue("$connectionType", settings.ConnectionType);
            cmd.Parameters.AddWithValue("$port", settings.PortName);
            cmd.Parameters.AddWithValue("$baud", settings.BaudRate);
            cmd.Parameters.AddWithValue("$data", settings.DataBits);
            cmd.Parameters.AddWithValue("$parity", settings.Parity);
            cmd.Parameters.AddWithValue("$stop", settings.StopBits);
            cmd.Parameters.AddWithValue("$ipAddress", settings.IpAddress);
            cmd.Parameters.AddWithValue("$tcpPort", settings.Port);
            cmd.Parameters.AddWithValue("$mode", settings.Mode);
            cmd.Parameters.AddWithValue("$isActive", settings.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$createdAt", settings.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$zeroReturn", settings.ZeroReturn ? 1 : 0);
            cmd.Parameters.AddWithValue("$scaleCode", settings.ScaleCode);
            cmd.Parameters.AddWithValue("$location", settings.Location);
            cmd.Parameters.AddWithValue("$isAutoCapture", settings.IsAutoCapture ? 1 : 0);
            cmd.Parameters.AddWithValue("$captureIntervalMs", settings.CaptureIntervalMs);
            cmd.Parameters.AddWithValue("$sampleScaleData", settings.SampleScaleData ?? string.Empty);
            cmd.Parameters.AddWithValue("$delimiter", settings.Delimiter ?? string.Empty);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<int> UpdateAsync(ScaleSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE scale_settings 
            SET scale_name = $scaleName,
                connection_type = $connectionType,
                port_name = $port,
                baud_rate = $baud,
                data_bits = $data,
                parity = $parity,
                stop_bits = $stop,
                ip_address = $ipAddress,
                port = $tcpPort,
                mode = $mode,
                is_active = $isActive,
                zero_return = $zeroReturn,
                scale_code = $scaleCode,
                location = $location,
                is_auto_capture = $isAutoCapture,
                capture_interval_ms = $captureIntervalMs,
                sample_scale_data = $sampleScaleData,
                delimiter = $delimiter
            WHERE id = $id";
            
            cmd.Parameters.AddWithValue("$id", settings.Id);
            cmd.Parameters.AddWithValue("$scaleName", settings.ScaleName);
            cmd.Parameters.AddWithValue("$connectionType", settings.ConnectionType);
            cmd.Parameters.AddWithValue("$port", settings.PortName);
            cmd.Parameters.AddWithValue("$baud", settings.BaudRate);
            cmd.Parameters.AddWithValue("$data", settings.DataBits);
            cmd.Parameters.AddWithValue("$parity", settings.Parity);
            cmd.Parameters.AddWithValue("$stop", settings.StopBits);
            cmd.Parameters.AddWithValue("$ipAddress", settings.IpAddress);
            cmd.Parameters.AddWithValue("$tcpPort", settings.Port);
            cmd.Parameters.AddWithValue("$mode", settings.Mode);
            cmd.Parameters.AddWithValue("$isActive", settings.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$zeroReturn", settings.ZeroReturn ? 1 : 0);
            cmd.Parameters.AddWithValue("$scaleCode", settings.ScaleCode);
            cmd.Parameters.AddWithValue("$location", settings.Location);
            cmd.Parameters.AddWithValue("$isAutoCapture", settings.IsAutoCapture ? 1 : 0);
            cmd.Parameters.AddWithValue("$captureIntervalMs", settings.CaptureIntervalMs);
            cmd.Parameters.AddWithValue("$sampleScaleData", settings.SampleScaleData ?? string.Empty);
            cmd.Parameters.AddWithValue("$delimiter", settings.Delimiter ?? string.Empty);
            
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM scale_settings WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task SaveAsync(ScaleSettings settings)
        {
            if (settings.Id > 0)
            {
                await UpdateAsync(settings);
            }
            else
            {
                await AddAsync(settings);
            }
        }

        public async Task<bool> PortExistsAsync(string portName, int? excludeId = null)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            
            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(1) FROM scale_settings WHERE port_name = $port AND id <> $id";
                cmd.Parameters.AddWithValue("$port", portName);
                cmd.Parameters.AddWithValue("$id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(1) FROM scale_settings WHERE port_name = $port";
                cmd.Parameters.AddWithValue("$port", portName);
            }
            
            var result = (long?)await cmd.ExecuteScalarAsync();
            return result > 0;
        }
    }
}
