using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.IO;

namespace MarketDataHub.Data
{
    /// <summary>
    /// Oracle database helper for reference data operations.
    /// Connects to the on-premises Oracle Exadata instance (LSEG-ORA-PROD01) which holds
    /// the enterprise reference data master (instrument reference, counterparty, corporate actions).
    /// 
    /// This is the "source of truth" for reference data. MarketDataHub syncs from Oracle nightly
    /// via the ETL pipeline. The SQL Server copy is a working replica for low-latency reads.
    /// 
    /// Uses OLE DB provider for Oracle (legacy - installed with Oracle Client 12c on all app servers).
    /// We should have migrated to ODP.NET years ago but the OLE DB driver is baked into our
    /// deployment scripts and changing it would break the Windows Service installer. - Stuart M. (2018)
    /// 
    /// Connection: LSEG-ORA-PROD01 / REFDATA schema / TNS name: LSEGREFPROD
    /// Failover: LSEG-ORA-PROD02 (Active Data Guard standby)
    /// </summary>
    public class OracleDatabaseHelper
    {
        private static string _oracleConnectionString = ConfigurationManager.ConnectionStrings["OracleRefDataDb"].ConnectionString;

        #region Reference Data Sync

        /// <summary>
        /// Full sync of instrument reference data from Oracle Exadata to SQL Server.
        /// Called nightly at 02:00 by the Windows Service ETL job.
        /// Takes ~45 minutes for full FTSE All-Share universe (~2,500 instruments).
        /// </summary>
        public static DataTable GetAllInstrumentReferenceData()
        {
            string sql = @"SELECT 
                i.INSTRUMENT_ID, i.ISIN_CODE, i.SEDOL_CODE, i.RIC_CODE, i.TICKER_SYMBOL,
                i.INSTRUMENT_NAME, i.EXCHANGE_CODE, i.ASSET_CLASS_CODE, i.CURRENCY_CODE,
                i.SECTOR_CODE, s.SECTOR_NAME, i.LISTING_DATE, i.DELISTING_DATE,
                i.IS_ACTIVE, i.COUNTRY_OF_INCORPORATION, i.COUNTRY_OF_RISK,
                i.LEI_CODE, i.CFI_CODE, i.FISN_CODE,
                i.SHARES_OUTSTANDING, i.FREE_FLOAT_SHARES, i.FREE_FLOAT_FACTOR,
                i.MARKET_CAP_USD, i.DIVIDEND_YIELD, i.PE_RATIO,
                i.CREATED_DATE, i.MODIFIED_DATE, i.MODIFIED_BY
            FROM REFDATA.INSTRUMENTS i
            LEFT JOIN REFDATA.SECTORS s ON i.SECTOR_CODE = s.SECTOR_CODE
            WHERE i.EXCHANGE_CODE IN ('LSE', 'AIM', 'TURQ', 'BATE', 'CHIX')
            ORDER BY i.INSTRUMENT_ID";

            return ExecuteOracleQuery(sql);
        }

        /// <summary>
        /// Get instruments modified since a given date (for incremental sync).
        /// Used by the hourly delta sync job.
        /// </summary>
        public static DataTable GetModifiedInstruments(DateTime since)
        {
            string sql = string.Format(@"SELECT 
                i.INSTRUMENT_ID, i.ISIN_CODE, i.SEDOL_CODE, i.RIC_CODE, i.TICKER_SYMBOL,
                i.INSTRUMENT_NAME, i.EXCHANGE_CODE, i.ASSET_CLASS_CODE, i.CURRENCY_CODE,
                i.SECTOR_CODE, i.LISTING_DATE, i.DELISTING_DATE, i.IS_ACTIVE,
                i.LEI_CODE, i.SHARES_OUTSTANDING, i.FREE_FLOAT_SHARES, i.FREE_FLOAT_FACTOR,
                i.MODIFIED_DATE, i.MODIFIED_BY
            FROM REFDATA.INSTRUMENTS i
            WHERE i.MODIFIED_DATE >= TO_DATE('{0}', 'YYYY-MM-DD HH24:MI:SS')
            AND i.EXCHANGE_CODE IN ('LSE', 'AIM', 'TURQ', 'BATE', 'CHIX')
            ORDER BY i.MODIFIED_DATE",
                since.ToString("yyyy-MM-dd HH:mm:ss"));

            return ExecuteOracleQuery(sql);
        }

        #endregion

        #region Corporate Actions

        /// <summary>
        /// Get pending corporate actions from the Oracle corporate actions master.
        /// Used by CorporateActionsService to apply dividends, splits, mergers etc.
        /// </summary>
        public static DataTable GetPendingCorporateActions(DateTime effectiveDate)
        {
            string sql = string.Format(@"SELECT 
                ca.ACTION_ID, ca.INSTRUMENT_ID, ca.ISIN_CODE, ca.ACTION_TYPE,
                ca.EFFECTIVE_DATE, ca.RECORD_DATE, ca.EX_DATE, ca.PAYMENT_DATE,
                ca.RATIO_OLD, ca.RATIO_NEW, ca.CASH_AMOUNT, ca.CURRENCY_CODE,
                ca.DESCRIPTION, ca.STATUS, ca.SOURCE_SYSTEM, ca.CREATED_DATE
            FROM REFDATA.CORPORATE_ACTIONS ca
            WHERE ca.EFFECTIVE_DATE = TO_DATE('{0}', 'YYYY-MM-DD')
            AND ca.STATUS = 'CONFIRMED'
            AND ca.IS_PROCESSED = 0
            ORDER BY ca.ACTION_TYPE, ca.INSTRUMENT_ID",
                effectiveDate.ToString("yyyy-MM-dd"));

            return ExecuteOracleQuery(sql);
        }

        /// <summary>
        /// Get historical corporate actions for a given instrument.
        /// </summary>
        public static DataTable GetCorporateActionHistory(string isin, int years = 5)
        {
            string sql = string.Format(@"SELECT 
                ca.ACTION_ID, ca.ACTION_TYPE, ca.EFFECTIVE_DATE, ca.RECORD_DATE,
                ca.RATIO_OLD, ca.RATIO_NEW, ca.CASH_AMOUNT, ca.CURRENCY_CODE,
                ca.DESCRIPTION, ca.STATUS
            FROM REFDATA.CORPORATE_ACTIONS ca
            WHERE ca.ISIN_CODE = '{0}'
            AND ca.EFFECTIVE_DATE >= ADD_MONTHS(SYSDATE, -{1})
            ORDER BY ca.EFFECTIVE_DATE DESC",
                isin, years * 12);

            return ExecuteOracleQuery(sql);
        }

        /// <summary>
        /// Mark a corporate action as processed in Oracle after applying it locally.
        /// </summary>
        public static void MarkCorporateActionProcessed(int actionId, string processedBy)
        {
            string sql = string.Format(@"UPDATE REFDATA.CORPORATE_ACTIONS 
                SET IS_PROCESSED = 1, PROCESSED_DATE = SYSDATE, PROCESSED_BY = '{1}'
                WHERE ACTION_ID = {0}",
                actionId, processedBy);

            ExecuteOracleNonQuery(sql);
        }

        #endregion

        #region Counterparty Data

        /// <summary>
        /// Get counterparty reference data for settlement and compliance.
        /// </summary>
        public static DataTable GetCounterparties()
        {
            string sql = @"SELECT 
                cp.COUNTERPARTY_ID, cp.LEI_CODE, cp.LEGAL_NAME, cp.SHORT_NAME,
                cp.COUNTRY_CODE, cp.ENTITY_TYPE, cp.BIC_CODE, cp.STATUS,
                cp.RISK_RATING, cp.AML_STATUS, cp.KYC_EXPIRY_DATE,
                cp.CREATED_DATE, cp.MODIFIED_DATE
            FROM REFDATA.COUNTERPARTIES cp
            WHERE cp.STATUS = 'ACTIVE'
            ORDER BY cp.SHORT_NAME";

            return ExecuteOracleQuery(sql);
        }

        #endregion

        #region Index Composition (Master)

        /// <summary>
        /// Get official index composition from Oracle (this is the master record).
        /// FTSE Russell maintains the composition in Oracle; SQL Server has a synced copy.
        /// </summary>
        public static DataTable GetOfficialIndexComposition(string indexCode)
        {
            string sql = string.Format(@"SELECT 
                ic.COMPOSITION_ID, ic.INDEX_CODE, ic.INDEX_NAME,
                ic.INSTRUMENT_ID, i.ISIN_CODE, i.RIC_CODE, i.TICKER_SYMBOL,
                ic.WEIGHT, ic.SHARES_IN_ISSUE, ic.FREE_FLOAT_FACTOR,
                ic.EFFECTIVE_DATE, ic.EXPIRY_DATE, ic.IS_ACTIVE
            FROM REFDATA.INDEX_COMPOSITION ic
            INNER JOIN REFDATA.INSTRUMENTS i ON ic.INSTRUMENT_ID = i.INSTRUMENT_ID
            WHERE ic.INDEX_CODE = '{0}'
            AND ic.IS_ACTIVE = 1
            AND ic.EFFECTIVE_DATE <= SYSDATE
            AND (ic.EXPIRY_DATE IS NULL OR ic.EXPIRY_DATE > SYSDATE)
            ORDER BY ic.WEIGHT DESC",
                indexCode);

            return ExecuteOracleQuery(sql);
        }

        #endregion

        #region Holiday Calendar

        /// <summary>
        /// Get market holiday calendar from Oracle reference data.
        /// Used by ETL scheduler to skip jobs on non-trading days.
        /// </summary>
        public static DataTable GetMarketHolidays(string exchangeCode, int year)
        {
            string sql = string.Format(@"SELECT 
                h.HOLIDAY_DATE, h.HOLIDAY_NAME, h.EXCHANGE_CODE, h.HOLIDAY_TYPE,
                h.IS_HALF_DAY, h.EARLY_CLOSE_TIME
            FROM REFDATA.MARKET_HOLIDAYS h
            WHERE h.EXCHANGE_CODE = '{0}'
            AND EXTRACT(YEAR FROM h.HOLIDAY_DATE) = {1}
            ORDER BY h.HOLIDAY_DATE",
                exchangeCode, year);

            return ExecuteOracleQuery(sql);
        }

        #endregion

        #region Private Helpers

        private static DataTable ExecuteOracleQuery(string sql)
        {
            DataTable dt = new DataTable();
            using (OleDbConnection conn = new OleDbConnection(_oracleConnectionString))
            {
                conn.Open();
                using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                {
                    cmd.CommandTimeout = 300; // 5 minute timeout for large reference data queries
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        dt.Load(reader);
                    }
                }
            }
            return dt;
        }

        private static int ExecuteOracleNonQuery(string sql)
        {
            using (OleDbConnection conn = new OleDbConnection(_oracleConnectionString))
            {
                conn.Open();
                using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                {
                    cmd.CommandTimeout = 120;
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private static object ExecuteOracleScalar(string sql)
        {
            using (OleDbConnection conn = new OleDbConnection(_oracleConnectionString))
            {
                conn.Open();
                using (OleDbCommand cmd = new OleDbCommand(sql, conn))
                {
                    return cmd.ExecuteScalar();
                }
            }
        }

        #endregion
    }
}
