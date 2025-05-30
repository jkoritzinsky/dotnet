// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.ComponentModel;
using System.Xml;
using System.Windows.Documents;

namespace System.Windows.Xps.Serialization
{
    /// <summary>
    /// 
    /// </summary>
    internal class DocumentSequenceSerializer :
                   ReachSerializer
    {
        /// <summary>
        /// 
        /// </summary>
        public
        DocumentSequenceSerializer(
            PackageSerializationManager   manager
            ):
        base(manager)
        {
            
        }

        /// <summary>
        ///
        /// </summary>
        internal
        override
        void
        PersistObjectData(
            SerializableObjectContext   serializableObjectContext
            )
        {
            String xmlnsForType = SerializationManager.GetXmlNSForType(typeof(FixedDocumentSequence));

            if( SerializationManager is XpsSerializationManager)
            {
                (SerializationManager as XpsSerializationManager).RegisterDocumentSequenceStart();
            }

            if(xmlnsForType == null)
            {
                XmlWriter.WriteStartElement(serializableObjectContext.Name);
            }
            else
            {
                XmlWriter.WriteStartElement(serializableObjectContext.Name,
                                            xmlnsForType);
            }

            {
                if(serializableObjectContext.IsComplexValue)
                {
                    //
                    // Pick the data for the PrintTicket if it existed
                    //
                    XpsSerializationPrintTicketRequiredEventArgs e = 
                    new XpsSerializationPrintTicketRequiredEventArgs(PrintTicketLevel.FixedDocumentSequencePrintTicket,
                                                                     0);

                    ((IXpsSerializationManager)SerializationManager).OnXPSSerializationPrintTicketRequired(e);

                    //
                    // Serialize the data for the PrintTicket
                    //
                    if(e.Modified)
                    {
                        if(e.PrintTicket != null)
                        {
                            PrintTicketSerializer serializer = new PrintTicketSerializer(SerializationManager);
                            serializer.SerializeObject(e.PrintTicket);
                        }
                    }

                    SerializeObjectCore(serializableObjectContext);
                }
                else
                {

                }
            }

            XmlWriter.WriteEndElement();
            XmlWriter = null;

            //
            // Signal to any registered callers that the Sequence has been serialized
            //
            XpsSerializationProgressChangedEventArgs progressEvent = 
            new XpsSerializationProgressChangedEventArgs(XpsWritingProgressChangeLevel.FixedDocumentSequenceWritingProgress,
                                                         0,
                                                         0,
                                                         null);

            if( SerializationManager is XpsSerializationManager)
            {
               (SerializationManager as XpsSerializationManager).RegisterDocumentSequenceEnd();
            }
            ((IXpsSerializationManager)SerializationManager).OnXPSSerializationProgressChanged(progressEvent);
        }
    
        /// <summary>
        /// 
        /// </summary>
        public
        override
        XmlWriter
        XmlWriter
        {
            get
            {
                if(base.XmlWriter == null)
                {
                    base.XmlWriter = SerializationManager.AcquireXmlWriter(typeof(FixedDocumentSequence));
                }

                return base.XmlWriter;
            }

            set
            {
                base.XmlWriter = null;
                SerializationManager.ReleaseXmlWriter(typeof(FixedDocumentSequence));
            }
        }

        /// <summary>
        ///
        /// </summary>
        internal
        override
        void
        WriteSerializedAttribute(
            SerializablePropertyContext serializablePropertyContext
            )
        {
            ArgumentNullException.ThrowIfNull(serializablePropertyContext);

            String attributeValue = String.Empty;

            attributeValue = GetValueOfAttributeAsString(serializablePropertyContext);

            if ( (attributeValue != null) && 
                 (attributeValue.Length > 0) )
            {
                //
                // Emit name="value" attribute
                //
                XmlWriter.WriteAttributeString(serializablePropertyContext.Name, attributeValue);
            }
        }

        internal
        String
        GetValueOfAttributeAsString(
            SerializablePropertyContext serializablePropertyContext
            )
        {
            ArgumentNullException.ThrowIfNull(serializablePropertyContext);

            String valueAsString                  = null;
            Object targetObjectContainingProperty = serializablePropertyContext.TargetObject;
            Object propertyValue                  = serializablePropertyContext.Value;

            if(propertyValue != null)
            {
                TypeConverter typeConverter = serializablePropertyContext.TypeConverter;

                valueAsString = typeConverter.ConvertToInvariantString(new XpsTokenContext(SerializationManager,
                                                                                             serializablePropertyContext),
                                                                       propertyValue);


                if (propertyValue is Type)
                {
                    int index = valueAsString.LastIndexOf('.');
                    valueAsString = string.Concat(
                        XpsSerializationManager.TypeOfString,
                        index > 0 ? valueAsString.AsSpan(index + 1) : valueAsString,
                        "}");
                }
            }
            else
            {
                valueAsString = XpsSerializationManager.NullString;
            }

            return valueAsString;
        }
    };
}
    

