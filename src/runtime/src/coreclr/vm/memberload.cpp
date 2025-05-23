// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// File: memberload.cpp
//


//

//
// ============================================================================

#include "common.h"
#include "clsload.hpp"
#include "method.hpp"
#include "class.h"
#include "object.h"
#include "field.h"
#include "util.hpp"
#include "excep.h"
#include "siginfo.hpp"
#include "threads.h"
#include "stublink.h"
#include "ecall.h"
#include "dllimport.h"
#include "jitinterface.h"
#include "eeconfig.h"
#include "log.h"
#include "fieldmarshaler.h"
#include "cgensys.h"
#include "gcheaputilities.h"
#include "dbginterface.h"
#include "comdelegate.h"
#include "sigformat.h"
#include "eeprofinterfaces.h"
#include "dllimportcallback.h"
#include "listlock.h"
#include "methodimpl.h"
#include "encee.h"
#include "comsynchronizable.h"
#include "customattribute.h"
#include "virtualcallstub.h"
#include "eeconfig.h"
#include "contractimpl.h"
#include "generics.h"
#include "instmethhash.h"
#include "typestring.h"

#ifndef DACCESS_COMPILE

void DECLSPEC_NORETURN MemberLoader::ThrowMissingFieldException(MethodTable* pMT, LPCSTR szMember)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_ANY;
        PRECONDITION(CheckPointer(pMT, NULL_OK));
        PRECONDITION(CheckPointer(szMember,NULL_OK));
    }
    CONTRACTL_END;

    LPCUTF8 szClassName;

    DefineFullyQualifiedNameForClass();
    if (pMT)
    {
        szClassName = GetFullyQualifiedNameForClass(pMT);
    }
    else
    {
        szClassName = "?";
    };


    LPUTF8 szFullName;
    MAKE_FULLY_QUALIFIED_MEMBER_NAME(szFullName, NULL, szClassName, (szMember?szMember:"?"), "");
    _ASSERTE(szFullName!=NULL);
    MAKE_WIDEPTR_FROMUTF8(szwFullName, szFullName);
    EX_THROW(EEMessageException, (kMissingFieldException, IDS_EE_MISSING_FIELD, szwFullName));
}

void DECLSPEC_NORETURN MemberLoader::ThrowMissingMethodException(MethodTable* pMT, LPCSTR szMember, ModuleBase *pModule, PCCOR_SIGNATURE pSig,DWORD cSig,const SigTypeContext *pTypeContext)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_ANY;
        PRECONDITION(CheckPointer(pMT,NULL_OK));
        PRECONDITION(CheckPointer(szMember,NULL_OK));
        PRECONDITION(CheckPointer(pSig,NULL_OK));
        PRECONDITION(CheckPointer(pModule,NULL_OK));
        PRECONDITION(CheckPointer(pTypeContext,NULL_OK));
    }
    CONTRACTL_END;
    LPCUTF8 szClassName;

    DefineFullyQualifiedNameForClass();
    if (pMT != NULL)
    {
        szClassName = GetFullyQualifiedNameForClass(pMT);
    }
    else
    {
        szClassName = "?";
    };

    if (szMember == NULL)
        szMember = "?";

    if (pSig && cSig && pModule && pModule->IsFullModule())
    {
        MetaSig tmp(pSig, cSig, static_cast<Module*>(pModule), pTypeContext);
        SigFormat sf(tmp, szMember, szClassName, NULL);
        MAKE_WIDEPTR_FROMUTF8(szwFullName, sf.GetCString());
        EX_THROW(EEMessageException, (kMissingMethodException, IDS_EE_MISSING_METHOD, szwFullName));
    }
    else
    {
        SString typeName;
        typeName.Printf("%s.%s", szClassName, szMember);
        EX_THROW(EEMessageException, (kMissingMethodException, IDS_EE_MISSING_METHOD, typeName.GetUnicode()));
    }
}

//---------------------------------------------------------------------------------------
//
void MemberLoader::GetDescFromMemberRef(ModuleBase * pModule,
                                        mdToken MemberRef,
                                        MethodDesc ** ppMD,
                                        FieldDesc ** ppFD,
                                        const SigTypeContext *pTypeContext,
                                        BOOL strictMetadataChecks,
                                        TypeHandle *ppTH,
                                        BOOL actualTypeRequired,
                                        PCCOR_SIGNATURE * ppTypeSig,
                                        ULONG * pcbTypeSig)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(MemberRef) == mdtMemberRef);
        PRECONDITION(ppMD != NULL && *ppMD == NULL);
        PRECONDITION(ppFD != NULL && *ppFD == NULL);
        PRECONDITION(ppTH != NULL && ppTH->IsNull());
        PRECONDITION(!((ppTypeSig == NULL) ^ (pcbTypeSig == NULL)));
    }
    CONTRACTL_END;

    // In lookup table?
    BOOL fIsMethod;
    TADDR pDatum = pModule->LookupMemberRef(MemberRef, &fIsMethod);

    if (pDatum != (TADDR)NULL)
    {
        if (!fIsMethod)
        {
            FieldDesc * pFD = dac_cast<PTR_FieldDesc>(pDatum);
            *ppFD = pFD;

            // Fields are not inherited so we can always return the exact type right away.
            *ppTH = pFD->GetEnclosingMethodTable();
            return;
        }
        else
        {
            MethodDesc * pMD = dac_cast<PTR_MethodDesc>(pDatum);
            pMD->CheckRestore();
            *ppMD = pMD;

            // We are done if the caller is not interested in actual type.
            if (!actualTypeRequired)
            {
                *ppTH = pMD->GetMethodTable();
                return;
            }
        }
    }

    // No, so do it the long way
    IMDInternalImport * pInternalImport = pModule->GetMDImport();

    mdTypeRef parent;
    IfFailThrow(pInternalImport->GetParentOfMemberRef(MemberRef, &parent));

    // If parent is a method def, then this is a varargs method and the
    // desc lives in the same module.
    if (TypeFromToken(parent) == mdtMethodDef)
    {
        // Return now if actualTypeRequired was set and the desc was cached
        if (pDatum != (TADDR)NULL)
        {
            *ppTH = dac_cast<PTR_MethodDesc>(pDatum)->GetMethodTable();
            return;
        }

        MethodDesc *pMethodDef = NULL;

        if (pModule->IsFullModule())
        {
            Module* pNormalModule = static_cast<Module*>(pModule);
            pMethodDef = pNormalModule->LookupMethodDef(parent);
            if (!pMethodDef)
            {
                // There is no value for this def so we haven't yet loaded the class.
                mdTypeDef typeDef;
                IfFailThrow(pInternalImport->GetParentToken(parent, &typeDef));

                // Make sure it is a typedef
                if (TypeFromToken(typeDef) != mdtTypeDef)
                {
                    COMPlusThrowHR(COR_E_BADIMAGEFORMAT, BFA_METHODDEF_WO_TYPEDEF_PARENT);
                }

                // load the class

                TypeHandle th = ClassLoader::LoadTypeDefThrowing(
                    pNormalModule,
                    typeDef,
                    ClassLoader::ThrowIfNotFound,
                    strictMetadataChecks ?
                        ClassLoader::FailIfUninstDefOrRef : ClassLoader::PermitUninstDefOrRef);

                // the class has been loaded and the method should be in the rid map!
                pMethodDef = pNormalModule->LookupMethodDef(parent);
            }
        }
        else
        {
            // Only normal modules may have MethodDefs
            COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
        }

        LPCUTF8 szMember;
        PCCOR_SIGNATURE pSig;
        DWORD cSig;

        IfFailThrow(pInternalImport->GetNameAndSigOfMemberRef(MemberRef, &pSig, &cSig, &szMember));

        BOOL fMissingMethod = FALSE;
        if (!pMethodDef)
        {
            fMissingMethod = TRUE;
        }
        else if (pMethodDef->HasClassOrMethodInstantiation())
        {
            // A memberref to a varargs method must not find a MethodDesc that is generic (as varargs methods may not be implemented on generics)
            fMissingMethod = TRUE;
        }
        else
        {
            // Ensure the found method matches up correctly
            PCCOR_SIGNATURE pMethodSig;
            DWORD       cMethodSig;

            pMethodDef->GetSig(&pMethodSig, &cMethodSig);
            if (!MetaSig::CompareMethodSigs(pSig, cSig, pModule, NULL, pMethodSig,
                                            cMethodSig, pModule, NULL, FALSE))
            {
                // If the signatures do not match, then the correct MethodDesc has not been found.
                fMissingMethod = TRUE;
            }
        }

        if (fMissingMethod)
        {
            ThrowMissingMethodException(
                (pMethodDef != NULL) ? pMethodDef->GetMethodTable() : NULL,
                szMember,
                pModule,
                pSig,
                cSig,
                pTypeContext);
        }

        pMethodDef->CheckRestore();

        *ppMD = pMethodDef;
        *ppTH = pMethodDef->GetMethodTable();

        pModule->StoreMemberRef(MemberRef, pMethodDef);
        return;
    }

    TypeHandle typeHnd;
    PCCOR_SIGNATURE pTypeSig = NULL;
    ULONG cTypeSig = 0;

    switch (TypeFromToken(parent))
    {
    case mdtModuleRef:
        {
            Module *pTargetModule = pModule->LoadModule(parent);
            if (pTargetModule == NULL)
                COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
            typeHnd = TypeHandle(pTargetModule->GetGlobalMethodTable());
            if (typeHnd.IsNull())
                COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
        }
        break;

    case mdtTypeDef:
    case mdtTypeRef:
        typeHnd = ClassLoader::LoadTypeDefOrRefThrowing(pModule, parent,
                                        ClassLoader::ThrowIfNotFound,
                                        strictMetadataChecks ?
                                            ClassLoader::FailIfUninstDefOrRef : ClassLoader::PermitUninstDefOrRef);
        break;

    case mdtTypeSpec:
        {
            IfFailThrow(pInternalImport->GetTypeSpecFromToken(parent, &pTypeSig, &cTypeSig));

            if (ppTypeSig != NULL)
            {
                *ppTypeSig = pTypeSig;
                *pcbTypeSig = cTypeSig;
            }

            SigPointer sigptr(pTypeSig, cTypeSig);
            typeHnd = sigptr.GetTypeHandleThrowing(pModule, pTypeContext);
        }
        break;

    default:
        COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
    }

    // Return now if actualTypeRequired was set and the desc was cached
    if (pDatum != (TADDR)NULL)
    {
        *ppTH = typeHnd;
        return;
    }

    // Now load the parent of the method ref
    MethodTable * pMT = typeHnd.GetMethodTable();

    // pMT will be null if typeHnd is a variable type
    if (pMT == NULL)
    {
        COMPlusThrowHR(COR_E_BADIMAGEFORMAT, BFA_METHODDEF_PARENT_NO_MEMBERS);
    }

    _ASSERTE(pMT != NULL);

    LPCUTF8     szMember;
    PCCOR_SIGNATURE pSig;
    DWORD       cSig;

    IfFailThrow(pInternalImport->GetNameAndSigOfMemberRef(MemberRef, &pSig, &cSig, &szMember));

    BOOL fIsField = isCallConv(
        MetaSig::GetCallingConvention(Signature(pSig, cSig)),
        IMAGE_CEE_CS_CALLCONV_FIELD);

    if (fIsField)
    {
        FieldDesc * pFD = MemberLoader::FindField(pMT, szMember, pSig, cSig, pModule);

        if (pFD == NULL)
            ThrowMissingFieldException(pMT, szMember);

        if (pFD->IsStatic() && pMT->HasGenericsStaticsInfo())
        {
           MethodTable* pFieldMT = pFD->GetApproxEnclosingMethodTable();
           DWORD index = pFieldMT->GetIndexForFieldDesc(pFD);
           pFD = pMT->GetFieldDescByIndex(index);
        }

        *ppFD = pFD;
        *ppTH = typeHnd;

        //@GENERICS: don't store FieldDescs for instantiated types
        //or we'll get the wrong one for another instantiation!
        if (!pMT->HasInstantiation())
        {
            pModule->StoreMemberRef(MemberRef, pFD);

            // Verify that the exact type returned here is same as exact type returned by the cached path
            _ASSERTE(TypeHandle(pFD->GetEnclosingMethodTable()) == *ppTH);
        }
    }
    else
    {
        // For array method signatures, the caller's signature contains "actual" types whereas the callee's signature has
        // formals (ELEMENT_TYPE_VAR 0 wherever the element type of the array occurs). So we need to pass in a substitution
        // built from the signature of the element type.
        Substitution sigSubst(pModule, SigPointer(), NULL);

        if (typeHnd.IsArray())
        {
            _ASSERTE(pTypeSig != NULL && cTypeSig != 0);

            SigPointer sigptr = SigPointer(pTypeSig, cTypeSig);
            CorElementType type;
            IfFailThrow(sigptr.GetElemType(&type));

            THROW_BAD_FORMAT_MAYBE(
                ((type == ELEMENT_TYPE_SZARRAY) || (type == ELEMENT_TYPE_ARRAY)),
                BFA_NOT_AN_ARRAY,
                pModule);
            sigSubst = Substitution(pModule, sigptr, NULL);
        }

        // Lookup the method in the class.
        MethodDesc * pMD = MemberLoader::FindMethod(pMT,
            szMember,
            pSig,
            cSig,
            pModule,
            MemberLoader::FM_Default,
            &sigSubst);
        if (pMD == NULL)
        {
            ThrowMissingMethodException(pMT, szMember, pModule, pSig, cSig, pTypeContext);
        }

        pMD->CheckRestore();

        *ppMD = pMD;
        *ppTH = typeHnd;

        // Don't store MethodDescs for instantiated types or we'll get
        // the wrong one for another instantiation!
        // The same thing happens for arrays as the same MemberRef can be used for multiple array types
        // e.g. the member ref in
        //   call void MyList<!0>[,]::Set(int32,int32,MyList<!0>)
        // could be used for the Set method in MyList<string>[,] and MyList<int32>[,], etc.
        // <NICE>use cache when memberref is closed (contains no free type parameters) as then it does identify</NICE>
        // a method-desc uniquely
        if (!pMD->HasClassOrMethodInstantiation() && !typeHnd.IsArray())
        {
            pModule->StoreMemberRef(MemberRef, pMD);

            // Return actual type only if caller asked for it
            if (!actualTypeRequired)
                *ppTH = pMD->GetMethodTable();
        }
    }
}

//---------------------------------------------------------------------------------------
//
MethodDesc * MemberLoader::GetMethodDescFromMemberRefAndType(ModuleBase * pModule,
                                                             mdToken MemberRef,
                                                             MethodTable * pMT)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(MemberRef) == mdtMemberRef);
    }
    CONTRACTL_END;

    //
    // Fraction of MemberLoader::GetDescFromMemberRef that we actually need here
    //

    IMDInternalImport * pInternalImport = pModule->GetMDImport();

    LPCUTF8     szMember;
    PCCOR_SIGNATURE pSig;
    DWORD       cSig;

    IfFailThrow(pInternalImport->GetNameAndSigOfMemberRef(MemberRef, &pSig, &cSig, &szMember));

    _ASSERTE(!isCallConv(MetaSig::GetCallingConvention(Signature(pSig, cSig)), IMAGE_CEE_CS_CALLCONV_FIELD));

    // For array method signatures, the caller's signature contains "actual" types whereas the callee's signature has
    // formals (ELEMENT_TYPE_VAR 0 wherever the element type of the array occurs). So we need to pass in a substitution
    // built from the signature of the element type.
    Substitution sigSubst(pModule, SigPointer(), NULL);

    if (pMT->IsArray())
    {
        mdTypeRef parent;
        IfFailThrow(pInternalImport->GetParentOfMemberRef(MemberRef, &parent));

        PCCOR_SIGNATURE pTypeSig = NULL;
        ULONG cTypeSig = 0;
        IfFailThrow(pInternalImport->GetTypeSpecFromToken(parent, &pTypeSig, &cTypeSig));
        _ASSERTE(pTypeSig != NULL && cTypeSig != 0);

        SigPointer sigptr = SigPointer(pTypeSig, cTypeSig);
        CorElementType type;
        IfFailThrow(sigptr.GetElemType(&type));

        _ASSERTE((type == ELEMENT_TYPE_SZARRAY) || (type == ELEMENT_TYPE_ARRAY));

        sigSubst = Substitution(pModule, sigptr, NULL);
    }

    // Lookup the method in the class.
    MethodDesc * pMD = MemberLoader::FindMethod(pMT,
        szMember,
        pSig,
        cSig,
        pModule,
        MemberLoader::FM_Default,
        &sigSubst);
    if (pMD == NULL)
    {
        ThrowMissingMethodException(pMT, szMember, pModule, pSig, cSig, NULL);
    }

    pMD->CheckRestore();

    return pMD;
}

//---------------------------------------------------------------------------------------
//
FieldDesc * MemberLoader::GetFieldDescFromMemberRefAndType(ModuleBase * pModule,
                                                           mdToken MemberRef,
                                                           MethodTable * pMT)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(MemberRef) == mdtMemberRef);
    }
    CONTRACTL_END;

    //
    // Fraction of MemberLoader::GetDescFromMemberRef that we actually need here
    //

    IMDInternalImport * pInternalImport = pModule->GetMDImport();

    LPCUTF8     szMember;
    PCCOR_SIGNATURE pSig;
    DWORD       cSig;

    IfFailThrow(pInternalImport->GetNameAndSigOfMemberRef(MemberRef, &pSig, &cSig, &szMember));

    _ASSERTE(isCallConv(MetaSig::GetCallingConvention(Signature(pSig, cSig)), IMAGE_CEE_CS_CALLCONV_FIELD));

    FieldDesc * pFD = MemberLoader::FindField(pMT, szMember, pSig, cSig, pModule);

    if (pFD == NULL)
        ThrowMissingFieldException(pMT, szMember);

    if (pFD->IsStatic() && pMT->HasGenericsStaticsInfo())
    {
        //
        // <NICE> this is duplicated logic GetFieldDescByIndex </NICE>
        //
        INDEBUG(mdFieldDef token = pFD->GetMemberDef();)

        DWORD pos = static_cast<DWORD>(pFD - (pMT->GetApproxFieldDescListRaw() + pMT->GetNumIntroducedInstanceFields()));
        _ASSERTE(pos >= 0 && pos < pMT->GetNumStaticFields());

        pFD = pMT->GetGenericsStaticFieldDescs() + pos;
        _ASSERTE(pFD->GetMemberDef() == token);
        _ASSERTE(!pFD->IsSharedByGenericInstantiations());
        _ASSERTE(pFD->GetEnclosingMethodTable() == pMT);
    }

    return pFD;
}

//---------------------------------------------------------------------------------------
//
MethodDesc* MemberLoader::GetMethodDescFromMethodDef(Module *pModule,
                                                     mdToken MethodDef,
                                                     BOOL strictMetadataChecks,
                                                     ClassLoadLevel owningTypeLoadLevel)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(MethodDef) == mdtMethodDef);
    }
    CONTRACTL_END;

    // In lookup table?
    MethodDesc * pMD = pModule->LookupMethodDef(MethodDef);
    if (!pMD)
    {
        // No, so do it the long way
        //
        // Notes on methodDefs to generic things
        //
        // For internal purposes we wish to resolve MethodDef from generic classes or for generic methods to
        // the corresponding fully uninstantiated descriptor.  For example, for
        //     class C<T> { void m(); }
        // then MethodDef for m resolves to a method descriptor for C<T>.m().  This is the
        // descriptor that gets stored in the RID map.
        //
        // Normal IL code that uses generic code cannot use MethodDefs in this way: all calls
        // to generic code must be emitted as MethodRefs and MethodSpecs.  However, at other
        // points in the codebase we need to resolve MethodDefs to generic uninstantiated
        // method descriptors, and this is the best place to implement that.
        //
        mdTypeDef typeDef;
        IfFailThrow(pModule->GetMDImport()->GetParentToken(MethodDef, &typeDef));

        TypeHandle th = ClassLoader::LoadTypeDefThrowing(
            pModule,
            typeDef,
            ClassLoader::ThrowIfNotFound,
            strictMetadataChecks ?
                ClassLoader::FailIfUninstDefOrRef : ClassLoader::PermitUninstDefOrRef);

        // The RID map should have been filled out if we fully loaded the class
        pMD = pModule->LookupMethodDef(MethodDef);

        if (pMD == NULL)
        {
            LPCUTF8 szMember;
            PCCOR_SIGNATURE pSig;
            DWORD cSig;

            IfFailThrow(pModule->GetMDImport()->GetSigOfMethodDef(MethodDef, &cSig, &pSig));
            IfFailThrow(pModule->GetMDImport()->GetNameOfMethodDef(MethodDef, &szMember));

            ThrowMissingMethodException(
                th.GetMethodTable(),
                szMember,
                pModule,
                pSig,
                cSig,
                NULL);
        }
    }

    pMD->CheckRestore(owningTypeLoadLevel);

#if 0
    // <TODO> Generics: enable this check after the findMethod call in the Zapper which passes
    // naked generic MethodDefs across the JIT interface is moved over into the EE</TODO>
    if (strictMetadataChecks && pDatum->GetNumGenericClassArgs() != 0)
    {
        THROW_BAD_FORMAT_MAYBE(!"Methods inside generic classes must be referenced using MemberRefs or MethodSpecs, even in the same module as the class", 0, pModule);
        COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
    }
#endif

    return pMD;
}

//---------------------------------------------------------------------------------------
//
FieldDesc* MemberLoader::GetFieldDescFromFieldDef(Module *pModule,
                                                  mdToken fieldDef,
                                                  BOOL strictMetadataChecks)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(fieldDef) == mdtFieldDef);
    }
    CONTRACTL_END;

    // In lookup table?
    FieldDesc* pFD = pModule->LookupFieldDef(fieldDef);
    if (!pFD)
    {
        // No, so do it the long way
        mdTypeDef typeDef;
        IfFailThrow(pModule->GetMDImport()->GetParentToken(fieldDef, &typeDef));

        // Load the class - that should set the desc in the rid map
        // Field defs to generic things resolve to the formal instantiation
        // without taking the type context into account.  They are only valid internally.
        // <TODO> check that we rule out field defs to generic things in IL streams elsewhere</TODO>

        TypeHandle th = ClassLoader::LoadTypeDefThrowing(
            pModule,
            typeDef,
            ClassLoader::ThrowIfNotFound,
            strictMetadataChecks ?
                ClassLoader::FailIfUninstDefOrRef : ClassLoader::PermitUninstDefOrRef);

        pFD = pModule->LookupFieldDef(fieldDef);
        if (pFD == NULL)
        {
            LPCUTF8 szMember;
            if (FAILED(pModule->GetMDImport()->GetNameOfFieldDef(fieldDef, &szMember)))
            {
                szMember = "Invalid FieldDef record";
            }
            ThrowMissingFieldException(th.GetMethodTable(), szMember);
        }
    }

    pFD->GetApproxEnclosingMethodTable()->CheckRestore();

#ifdef FEATURE_METADATA_UPDATER
    if (pModule->IsEditAndContinueEnabled() && pFD->IsEnCNew())
    {
        EnCFieldDesc *pEnCFD = (EnCFieldDesc*)pFD;
        // we may not have the full FieldDesc info at applyEnC time because we don't
        // have a thread so can't do things like load classes (due to possible exceptions)
        if (pEnCFD->NeedsFixup())
        {
            GCX_COOP();
            pEnCFD->Fixup(fieldDef);
        }
    }
#endif // FEATURE_METADATA_UPDATER

    return pFD;
}

//---------------------------------------------------------------------------------------
//
MethodDesc *
MemberLoader::GetMethodDescFromMemberDefOrRefOrSpec(
    Module *               pModule,
    mdMemberRef            MemberRef,
    const SigTypeContext * pTypeContext,
    BOOL                   strictMetadataChecks,
                        // Normally true - the zapper is one exception.  Throw an exception if no generic method args
                        // given for a generic method, otherwise return the 'generic' instantiation
    BOOL                   allowInstParam,
    ClassLoadLevel         owningTypeLoadLevel)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        PRECONDITION(CheckPointer(pModule));
    }
    CONTRACTL_END;

    IMDInternalImport *pInternalImport = pModule->GetMDImport();
    if(!pInternalImport->IsValidToken(MemberRef))
    {
        // The exception type and message preserved for compatibility
        THROW_BAD_FORMAT(BFA_INVALID_METHOD_TOKEN, pModule);
    }

    MethodDesc * pMD = NULL;
    FieldDesc * pFD = NULL;
    TypeHandle th;

    switch (TypeFromToken(MemberRef))
    {
    case mdtMethodDef:
        pMD = GetMethodDescFromMethodDef(pModule, MemberRef, strictMetadataChecks, owningTypeLoadLevel);
        th = pMD->GetMethodTable();
        break;

    case mdtMemberRef:
        GetDescFromMemberRef(pModule, MemberRef, &pMD, &pFD, pTypeContext, strictMetadataChecks, &th);

        if (pMD == NULL)
        {
            // The exception type and message preserved for compatibility
            EX_THROW(EEMessageException, (kMissingMethodException, IDS_EE_MISSING_METHOD, W("?")));
        }
        break;

    case mdtMethodSpec:
        return GetMethodDescFromMethodSpec(pModule, MemberRef, pTypeContext, strictMetadataChecks, allowInstParam, &th);

    default:
        COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
    }

    // Apply the method instantiation if any.  If not applying strictMetadataChecks we
    // generate the "generic" instantiation - this is used by FuncEval.
    //
    // For generic code this call will return an instantiating stub where needed.  If the method
    // is a generic method then instantiate it with the given parameters.
    // For non-generic code this will just return pDatum
    return MethodDesc::FindOrCreateAssociatedMethodDesc(
        pMD,
        th.GetMethodTable(),
        FALSE /* don't get unboxing entry point */,
        strictMetadataChecks ? Instantiation() : pMD->LoadMethodInstantiation(),
        allowInstParam,
        /* forceRemotableMethod */ FALSE,
        /* allowCreate */ TRUE,
        AsyncVariantLookup::MatchingAsyncVariant,
        /* level */ owningTypeLoadLevel);
} // MemberLoader::GetMethodDescFromMemberDefOrRefOrSpec

//---------------------------------------------------------------------------------------
//
MethodDesc * MemberLoader::GetMethodDescFromMethodSpec(Module * pModule,
                                                       mdToken MethodSpec,
                                                       const SigTypeContext *pTypeContext,
                                                       BOOL strictMetadataChecks,
                                                       BOOL allowInstParam,
                                                       TypeHandle *ppTH,
                                                       BOOL actualTypeRequired,
                                                       PCCOR_SIGNATURE * ppTypeSig,
                                                       ULONG * pcbTypeSig,
                                                       PCCOR_SIGNATURE * ppMethodSig,
                                                       ULONG * pcbMethodSig)

{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(TypeFromToken(MethodSpec) == mdtMethodSpec);
        PRECONDITION(ppTH != NULL && ppTH->IsNull());
        PRECONDITION(!((ppTypeSig == NULL) ^ (pcbTypeSig == NULL)));
        PRECONDITION(!((ppMethodSig == NULL) ^ (pcbMethodSig == NULL)));
    }
    CONTRACTL_END;

    CQuickBytes qbGenericMethodArgs;

    mdToken genericMember;
    PCCOR_SIGNATURE pSig;
    ULONG cSig;

    IMDInternalImport * pInternalImport = pModule->GetMDImport();

    // Get the member def/ref and instantiation signature
    IfFailThrow(pInternalImport->GetMethodSpecProps(MethodSpec, &genericMember, &pSig, &cSig));

    if (ppMethodSig != NULL)
    {
        *ppMethodSig = pSig;
        *pcbMethodSig = cSig;
    }

    SigPointer sp(pSig, cSig);

    BYTE etype;
    IfFailThrow(sp.GetByte(&etype));

    // Load the generic method instantiation
    THROW_BAD_FORMAT_MAYBE(etype == (BYTE)IMAGE_CEE_CS_CALLCONV_GENERICINST, 0, pModule);

    uint32_t nGenericMethodArgs = 0;
    IfFailThrow(sp.GetData(&nGenericMethodArgs));

    DWORD cbAllocSize = 0;
    if (!ClrSafeInt<DWORD>::multiply(nGenericMethodArgs, sizeof(TypeHandle), cbAllocSize))
    {
        COMPlusThrowHR(COR_E_OVERFLOW);
    }

    TypeHandle *genericMethodArgs = reinterpret_cast<TypeHandle *>(qbGenericMethodArgs.AllocThrows(cbAllocSize));

    for (uint32_t i = 0; i < nGenericMethodArgs; i++)
    {
        genericMethodArgs[i] = sp.GetTypeHandleThrowing(pModule, pTypeContext);
        _ASSERTE (!genericMethodArgs[i].IsNull());
        IfFailThrow(sp.SkipExactlyOne());
    }

    MethodDesc * pMD = NULL;
    FieldDesc * pFD = NULL;

    switch (TypeFromToken(genericMember))
    {
    case mdtMethodDef:
        pMD = GetMethodDescFromMethodDef(pModule, genericMember, strictMetadataChecks);
        *ppTH = pMD->GetMethodTable();
        break;

    case mdtMemberRef:
        GetDescFromMemberRef(pModule, genericMember, &pMD, &pFD, pTypeContext, strictMetadataChecks, ppTH,
            actualTypeRequired, ppTypeSig, pcbTypeSig);

        if (pMD == NULL)
        {
            // The exception type and message preserved for compatibility
            EX_THROW(EEMessageException, (kMissingMethodException, IDS_EE_MISSING_METHOD, W("?")));
        }
        break;

    default:
        // The exception type and message preserved for compatibility
        THROW_BAD_FORMAT(
            BFA_EXPECTED_METHODDEF_OR_MEMBERREF,
            pModule);
    }

    return MethodDesc::FindOrCreateAssociatedMethodDesc(
        pMD,
        ppTH->GetMethodTable(),
        FALSE /* don't get unboxing entry point */,
        Instantiation(genericMethodArgs, nGenericMethodArgs),
        allowInstParam);
}

//---------------------------------------------------------------------------------------
//
MethodDesc *
MemberLoader::GetMethodDescFromMethodDef(
    Module *      pModule,
    mdMethodDef   MethodDef,    // MethodDef token
    Instantiation classInst,    // Generic arguments for declaring class
    Instantiation methodInst,   // Generic arguments for declaring method
    BOOL forceRemotable /* = FALSE */)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        PRECONDITION(CheckPointer(pModule));
        PRECONDITION(TypeFromToken(MethodDef) == mdtMethodDef);
    }
    CONTRACTL_END;

    // Get the generic method definition.  The functions above are guaranteed to
    // return the generic definition when given a MethodDef.
    MethodDesc* pDefMD = GetMethodDescFromMethodDef(pModule, MethodDef, FALSE);
    if (pDefMD->GetNumGenericMethodArgs() != methodInst.GetNumArgs())
    {
        COMPlusThrowHR(COR_E_TARGETPARAMCOUNT);
    }

    // If the class isn't generic then LoadGenericInstantiation just checks that
    // we're not supplying type parameters and then returns us the class as a type handle
    MethodTable *pMT = ClassLoader::LoadGenericInstantiationThrowing(
        pModule, pDefMD->GetMethodTable()->GetCl(), classInst).AsMethodTable();

    // Apply the instantiations (if any).
    MethodDesc *pMD = MethodDesc::FindOrCreateAssociatedMethodDesc(pDefMD, pMT,
                                                                   FALSE, /* don't get unboxing entry point */
                                                                   methodInst,
                                                                   FALSE /* no allowInstParam */,
                                                                   forceRemotable);

    return pMD;
}

FieldDesc* MemberLoader::GetFieldDescFromMemberDefOrRef(
    Module *pModule,
    mdMemberRef MemberDefOrRef,
    const SigTypeContext *pTypeContext,
    BOOL strictMetadataChecks  // Normally true - reflection is the one exception.  Throw an exception if no generic method args given for a generic field, otherwise return the 'generic' instantiation
    )
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
    }
    CONTRACTL_END;

    FieldDesc * pFD = NULL;
    MethodDesc * pMD = NULL;
    TypeHandle th;

    switch (TypeFromToken(MemberDefOrRef))
    {
    case mdtFieldDef:
        pFD = GetFieldDescFromFieldDef(pModule, MemberDefOrRef, strictMetadataChecks);
        break;

    case mdtMemberRef:
        GetDescFromMemberRef(
            pModule, MemberDefOrRef, &pMD, &pFD, pTypeContext, strictMetadataChecks, &th);

        if (!pFD)
        {
            // The exception type and message preserved for compatibility
            COMPlusThrow(kMissingFieldException, W("Arg_MissingFieldException"));
        }
        break;

    default:
        COMPlusThrowHR(COR_E_BADIMAGEFORMAT);
    }

    return pFD;
}

//*******************************************************************************
BOOL MemberLoader::FM_PossibleToSkipMethod(FM_Flags flags)
{
    LIMITED_METHOD_CONTRACT;

    return ((flags & FM_SpecialVirtualMask) || (flags & FM_SpecialAccessMask));
}

//*******************************************************************************
BOOL MemberLoader::FM_ShouldSkipMethod(DWORD dwAttrs, FM_Flags flags)
{
    LIMITED_METHOD_CONTRACT;

    BOOL retVal = FALSE;

    // If we have any special selection flags, then we need to check a little deeper.
    if (flags & FM_SpecialVirtualMask)
    {
        if (((flags & FM_ExcludeVirtual) && IsMdVirtual(dwAttrs)) ||
            ((flags & FM_ExcludeNonVirtual) && !IsMdVirtual(dwAttrs)))
        {
            retVal = TRUE;
        }
    }

    // This makes for quick shifting in determining if an access mask bit matches
    static_assert_no_msg((FM_ExcludePrivateScope >> 0x4) == 0x1);

    if (flags & FM_SpecialAccessMask)
    {
        DWORD dwAccess = dwAttrs & mdMemberAccessMask;
        if ((1 << dwAccess) & ((DWORD)(flags & FM_SpecialAccessMask) >> 0x4))
        {
            retVal = TRUE;
        }
    }

    // Ensure that this function is kept in sync with FM_PossibleToSkipMethod
    CONSISTENCY_CHECK(FM_PossibleToSkipMethod(flags) || !retVal);

    return retVal;
}

//*******************************************************************************
// Given a signature, and a method declared on a class or on a parent of a class,
// find out if the signature matches the method.
//
// In the normal non-generic case, we can simply perform a signature check,
// but with generics, we need to have a properly set up Substitution, so that
// we have a correct set of types to compare with. The idea is that either the current
// EEClass matches up with the methoddesc, or a parent EEClass will match up.
static BOOL CompareMethodSigWithCorrectSubstitution(
            PCCOR_SIGNATURE pSignature,
            DWORD       cSignature,
            ModuleBase* pModule,
            MethodDesc *pCurDeclMD,
            const Substitution *pDefSubst,
            MethodTable *pCurMT
        )
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        MODE_ANY;
        INJECT_FAULT(COMPlusThrowOM());
    }
    CONTRACTL_END

    MethodTable *pCurDeclMT = pCurDeclMD->GetMethodTable();
    BOOL fNeedsSubstitutionUpdateDueToInstantiationDifferences = pCurDeclMT->HasInstantiation() && pCurDeclMT != pCurMT->GetCanonicalMethodTable();
    if (!fNeedsSubstitutionUpdateDueToInstantiationDifferences)
    {
        PCCOR_SIGNATURE pCurMethodSig;
        DWORD       cCurMethodSig;

        pCurDeclMD->GetSig(&pCurMethodSig, &cCurMethodSig);
        return MetaSig::CompareMethodSigs(pSignature, cSignature, pModule, NULL, pCurMethodSig,
                                       cCurMethodSig, pCurDeclMD->GetModule(), pDefSubst, FALSE);
    }
    else
    {
        MethodTable *pParentMT = pCurMT->GetParentMethodTable();
        if (pParentMT != NULL)
        {
            Substitution subst2 = pCurMT->GetSubstitutionForParent(pDefSubst);

            return CompareMethodSigWithCorrectSubstitution(pSignature, cSignature, pModule, pCurDeclMD, &subst2, pParentMT);
        }
        return FALSE;
    }
}

//*******************************************************************************
// Finds a method by name and signature, where scope is the scope in which the
// signature is defined.
MethodDesc *
MemberLoader::FindMethod(
    MethodTable* pMT,
    LPCUTF8 pszName,
    PCCOR_SIGNATURE pSignature, DWORD cSignature,
    ModuleBase* pModule,
    FM_Flags flags,                       // = FM_Default
    const Substitution *pDefSubst)        // = NULL
{
    CONTRACT (MethodDesc *) {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    } CONTRACT_END;

    LOG((LF_LOADER, LL_INFO10000, "ML::FM pMT:%p for %s sig:%p sigLen:%u\n",
        pMT, pszName, pSignature, cSignature));

    // Retrieve the right comparison function to use.
    UTF8StringCompareFuncPtr StrCompFunc = FM_GetStrCompFunc(flags);

    const bool canSkipMethod = FM_PossibleToSkipMethod(flags);
    const bool ignoreName = (flags & FM_IgnoreName) != 0;

    // Statistically it's most likely for a method to be found in non-vtable portion of this class's members, then in the
    // vtable of this class's declared members, then in the inherited portion of the vtable, so we search backwards.

    // For value classes, if it's a value class method, we want to return the duplicated MethodDesc, not the one in the vtable
    // section.  We'll find the one in the duplicate section before the one in the vtable section, so we're ok.

    // Search non-vtable portion of this class first

    MethodTable::MethodIterator it(pMT);

    // Move the iterator to the appropriate starting point. It is imporant to search from the end
    // because hide-by-sig methods found in child types must be matched before the methods they
    // may be hiding in parent types.
    it.MoveToEnd();

    // Iterate through the methods of the current type searching for a match.
    for (; it.IsValid(); it.Prev())
    {
        MethodDesc *pCurDeclMD = it.GetDeclMethodDesc();

        LOG((LF_LOADER, LL_INFO100000, "ML::FM Considering %s::%s, pMD:%p\n",
            pCurDeclMD->m_pszDebugClassName, pCurDeclMD->m_pszDebugMethodName, pCurDeclMD));

#ifdef _DEBUG
        MethodTable *pCurDeclMT = pCurDeclMD->GetMethodTable();
        CONSISTENCY_CHECK(!pMT->IsInterface() || pCurDeclMT == pMT->GetCanonicalMethodTable());
#endif

        if (canSkipMethod && FM_ShouldSkipMethod(pCurDeclMD->GetAttrs(), flags))
        {
            continue;
        }

        if (ignoreName || StrCompFunc(pszName, pCurDeclMD->GetNameThrowing()) == 0)
        {
            if (CompareMethodSigWithCorrectSubstitution(pSignature, cSignature, pModule, pCurDeclMD, pDefSubst, pMT))
            {
                RETURN pCurDeclMD;
            }
        }
    }

    // No inheritance on value types or interfaces
    if (pMT->IsValueType() || pMT->IsInterface())
    {
        RETURN NULL;
    }

    // Recurse up the hierarchy if the method was not found.
    //<TODO>@todo: This routine might be factored slightly to improve perf.</TODO>
    CONSISTENCY_CHECK(pMT->CheckLoadLevel(CLASS_LOAD_APPROXPARENTS));

    MethodDesc* md = NULL;
    MethodTable* pParentMT = pMT->GetParentMethodTable();
    if (pParentMT != NULL)
    {
        Substitution subst2 = pMT->GetSubstitutionForParent(pDefSubst);

        md = MemberLoader::FindMethod(pParentMT, pszName, pSignature, cSignature, pModule, flags, &subst2);

        // Don't inherit constructors from parent classes.  It is important to forbid this,
        // because the JIT needs to get the class handle from the memberRef, and when the
        // constructor is inherited, the JIT will get the class handle for the parent class
        // (and not allocate enough space, etc.).
        if (md != NULL
            && IsMdInstanceInitializer(md->GetAttrs(), pszName))
        {
            md = NULL;
        }
    }

#ifdef FEATURE_METADATA_UPDATER
    // In the event the method wasn't found and the current module has
    // EnC enabled, try the slow path and go through all available methods.
    if (md == NULL
        && pMT->GetModule()->IsEditAndContinueEnabled())
    {
        LOG((LF_LOADER, LL_INFO100000, "ML::FM Falling back to EnC slow path\n"));

        MethodTable::IntroducedMethodIterator itMethods(pMT, FALSE);
        for (; itMethods.IsValid(); itMethods.Next())
        {
            MethodDesc* pCurDeclMD = itMethods.GetMethodDesc();

#ifdef _DEBUG
            MethodTable *pCurDeclMT = pCurDeclMD->GetMethodTable();
            CONSISTENCY_CHECK(!pMT->IsInterface() || pCurDeclMT == pMT->GetCanonicalMethodTable());
#endif

            if (canSkipMethod && FM_ShouldSkipMethod(pCurDeclMD->GetAttrs(), flags))
            {
                continue;
            }

            LOG((LF_LOADER, LL_INFO100000, "ML::FM EnC - Considering %s::%s, pMD:%p\n",
                pCurDeclMD->m_pszDebugClassName, pCurDeclMD->m_pszDebugMethodName, pCurDeclMD));

            if (ignoreName || StrCompFunc(pszName, pCurDeclMD->GetNameThrowing()) == 0)
            {
                if (CompareMethodSigWithCorrectSubstitution(pSignature, cSignature, pModule, pCurDeclMD, pDefSubst, pMT))
                {
                    RETURN pCurDeclMD;
                }
            }
        }
    }
#endif // FEATURE_METADATA_UPDATER

    RETURN md;
}

//*******************************************************************************
// This will return the MethodDesc that implements the interface method <pInterface,slotNum>.
MethodDesc *
MemberLoader::FindMethodForInterfaceSlot(MethodTable * pMT, MethodTable *pInterface, WORD slotNum)
{
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        PRECONDITION(CheckPointer(pInterface));
        PRECONDITION(pInterface->IsInterface());
        PRECONDITION(slotNum < pInterface->GetNumVirtuals());
    } CONTRACTL_END;

    MethodDesc *pMDRet = NULL;

    DispatchSlot ds(pMT->FindDispatchSlot(pInterface->GetTypeID(), (UINT32)slotNum, FALSE /* throwOnConflict */));
    if (!ds.IsNull()) {
        pMDRet = ds.GetMethodDesc();
    }

    CONSISTENCY_CHECK(CheckPointer(pMDRet));
    return pMDRet;
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindMethod(MethodTable * pMT, LPCUTF8 pwzName, LPHARDCODEDMETASIG pwzSignature, FM_Flags flags)
    {
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    } CONTRACTL_END;

    Signature sig = CoreLibBinder::GetSignature(pwzSignature);

    return FindMethod(pMT, pwzName, sig.GetRawSig(), sig.GetRawSigLen(), CoreLibBinder::GetModule(), flags);
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindMethod(MethodTable * pMT, mdMethodDef mb)
{
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    } CONTRACTL_END;

    // We have the EEClass (this) and so lets just look this up in the ridmap.
    MethodDesc *pMD     = NULL;
    Module     *pModule = pMT->GetModule();
    _ASSERTE(pModule != NULL);

    if (TypeFromToken(mb) == mdtMemberRef)
        pMD = pModule->LookupMemberRefAsMethod(mb);
    else
        pMD = pModule->LookupMethodDef(mb);

    if (pMD != NULL)
        pMD->CheckRestore();

    return pMD;
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindMethodByName(MethodTable * pMT, LPCUTF8 pszName, FM_Flags flags)
{
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        PRECONDITION(!pMT->IsArray());
        MODE_ANY;
    } CONTRACTL_END;

    // Retrieve the right comparison function to use.
    UTF8StringCompareFuncPtr StrCompFunc = FM_GetStrCompFunc(flags);

    // Scan all classes in the hierarchy, starting at the current class and
    // moving back up towards the base.
    while (pMT != NULL)
    {
        MethodDesc *pRetMD = NULL;

        // Iterate through the methods searching for a match.
        MethodTable::MethodIterator it(pMT);
        it.MoveToEnd();
        for (; it.IsValid(); it.Prev())
        {
            MethodDesc *pCurMD = it.GetDeclMethodDesc();

            if (pCurMD != NULL)
            {
                // If we're working from the end of the vtable, we'll cover all the non-virtuals
                // first, and so if we're supposed to ignore virtuals (see setting of the flag
                // below) then we can just break out of the loop and go to the parent.
                if ((flags & FM_ExcludeVirtual) && pCurMD->IsVirtual())
                {
                    break;
                }

                if (FM_PossibleToSkipMethod(flags) && FM_ShouldSkipMethod(pCurMD->GetAttrs(), flags))
                {
                    continue;
                }

                if (StrCompFunc(pszName, pCurMD->GetNameOnNonArrayClass()) == 0)
                {
                    if (pRetMD != NULL)
                    {
                        _ASSERTE(flags & FM_Unique);

                        // Found another method of this name but FM_Unique was given.
                        return NULL;
                    }

                    pRetMD = it.GetMethodDesc();
                    pRetMD->CheckRestore();

                    // Let's always finish iterating through this MT for FM_Unique to reveal overloads, i.e.
                    // methods with the same name. Returning the first/last method of the given name
                    // may in some cases work but it depends on the vtable order which is something we
                    // do not want. It can be easily broken by a seemingly unrelated change.
                    if (!(flags & FM_Unique))
                        return pRetMD;
                }
            }
        }

        if (pRetMD != NULL)
            return pRetMD;

        // Check the parent type for a matching method.
        pMT = pMT->GetParentMethodTable();

        // There is no need to check virtuals for parent types, since by definition they have the same name.
        //
        // Warning: This is not entirely true as virtuals can be overridden explicitly regardless of their name.
        // We should be fine though as long as we do not use this code to find arbitrary user-defined methods.
        flags = (FM_Flags)(flags | FM_ExcludeVirtual);
    }

    return NULL;
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindPropertyMethod(MethodTable * pMT, LPCUTF8 pszName, EnumPropertyMethods Method, FM_Flags flags)
{
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
        PRECONDITION(Method < 2);
    } CONTRACTL_END;

    // The format strings for the getter and setter. These must stay in synch with the
    // EnumPropertyMethods enum defined in class.h
    static const LPCUTF8 aFormatStrings[] =
    {
        "get_%s",
        "set_%s"
    };

    CQuickBytes qbMethName;
    size_t len = strlen(pszName) + strlen(aFormatStrings[Method]) + 1;
    LPUTF8 strMethName = (LPUTF8) qbMethName.AllocThrows(len);
    sprintf_s(strMethName, len, aFormatStrings[Method], pszName);

    return FindMethodByName(pMT, strMethName, flags);
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindEventMethod(MethodTable * pMT, LPCUTF8 pszName, EnumEventMethods Method, FM_Flags flags)
    {
    CONTRACTL {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
        PRECONDITION(Method < 3);
    } CONTRACTL_END;

    // The format strings for the getter and setter. These must stay in synch with the
    // EnumPropertyMethods enum defined in class.h
    static const LPCUTF8 aFormatStrings[] =
    {
        "add_%s",
        "remove_%s",
        "raise_%s"
    };

    CQuickBytes qbMethName;
    size_t len = strlen(pszName) + strlen(aFormatStrings[Method]) + 1;
    LPUTF8 strMethName = (LPUTF8) qbMethName.AllocThrows(len);
    sprintf_s(strMethName, len, aFormatStrings[Method], pszName);

    return FindMethodByName(pMT, strMethName, flags);
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindConstructor(MethodTable * pMT, LPHARDCODEDMETASIG pwzSignature)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    }
    CONTRACTL_END

    Signature sig = CoreLibBinder::GetSignature(pwzSignature);

    return FindConstructor(pMT, sig.GetRawSig(), sig.GetRawSigLen(), CoreLibBinder::GetModule());
}

//*******************************************************************************
MethodDesc *
MemberLoader::FindConstructor(MethodTable * pMT, PCCOR_SIGNATURE pSignature,DWORD cSignature, Module* pModule)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    }
    CONTRACTL_END

    // Array classes don't have metadata
    if (pMT->IsArray())
        return NULL;

    MethodTable::MethodIterator it(pMT);

    for (it.MoveTo(it.GetNumVirtuals()); it.IsValid(); it.Next())
    {
        _ASSERTE(!it.IsVirtual());

        MethodDesc *pCurMethod = it.GetMethodDesc();
        if (pCurMethod == NULL)
        {
            continue;
        }

        // Don't want class initializers.
        if (pCurMethod->IsStatic())
        {
            continue;
        }

        DWORD dwCurMethodAttrs = pCurMethod->GetAttrs();
        if (!IsMdRTSpecialName(dwCurMethodAttrs))
        {
            continue;
        }

        // Find only the constructor for this object
        _ASSERTE(pCurMethod->GetMethodTable() == pMT->GetCanonicalMethodTable());

        PCCOR_SIGNATURE pCurMethodSig;
        DWORD cCurMethodSig;
        pCurMethod->GetSig(&pCurMethodSig, &cCurMethodSig);

        if (MetaSig::CompareMethodSigs(pSignature, cSignature, pModule, NULL, pCurMethodSig, cCurMethodSig, pCurMethod->GetModule(), NULL, FALSE))
        {
            return pCurMethod;
        }
    }

    return NULL;
}

#endif // DACCESS_COMPILE

FieldDesc *
MemberLoader::FindField(MethodTable* pMT, LPCUTF8 pszName, PCCOR_SIGNATURE pSignature, DWORD cSignature, ModuleBase* pModule, BOOL bCaseSensitive)
{
    CONTRACTL
    {
        THROWS;
        GC_TRIGGERS;
        INJECT_FAULT(COMPlusThrowOM(););
        MODE_ANY;
    }
    CONTRACTL_END

    LOG((LF_LOADER, LL_INFO100000, "ML::FF '%s' in pModule:%p pMT:%p, %s\n",
        pszName, pModule, pMT, pMT->GetDebugClassName()));

    // Array classes don't have fields, and don't have metadata
    if (pMT->IsArray())
        return NULL;

    IMDInternalImport *pInternalImport = pMT->GetMDImport(); // All explicitly declared fields in this class will have the same scope
    CONSISTENCY_CHECK(pMT->CheckLoadLevel(CLASS_LOAD_APPROXPARENTS));

    // Retrieve the right comparison function to use.
    UTF8StringCompareFuncPtr StrCompFunc = bCaseSensitive ? strcmp : stricmpUTF8;

    EEClass * pClass = pMT->GetClass();
    MethodTable *pParentMT = pMT->GetParentMethodTable();

    // Scan the FieldDescs of this class
    DWORD fieldDescCount = (pParentMT != NULL)
        ? pClass->GetNumInstanceFields() - pParentMT->GetNumInstanceFields() + pClass->GetNumStaticFields()
        : pClass->GetNumInstanceFields() + pClass->GetNumStaticFields();

    PTR_FieldDesc pFieldDescList = pClass->GetFieldDescList();

    LPCUTF8 szMemberName;
    mdFieldDef mdField;
    for (DWORD i = 0; i < fieldDescCount; i++)
    {
        FieldDesc * pFD = &pFieldDescList[i];
        _ASSERTE(pFD!=NULL);

        // Check is valid FieldDesc, and not some random memory
        INDEBUGIMPL(pFD->GetApproxEnclosingMethodTable()->SanityCheck());

        mdField = pFD->GetMemberDef();
        IfFailThrow(pInternalImport->GetNameOfFieldDef(mdField, &szMemberName));

        if (StrCompFunc(szMemberName, pszName) != 0)
            continue;

        if (pSignature != NULL)
        {
            PCCOR_SIGNATURE pMemberSig;
            DWORD       cMemberSig;
            IfFailThrow(pInternalImport->GetSigOfFieldDef(mdField, &cMemberSig, &pMemberSig));

            if (!MetaSig::CompareFieldSigs(
                    pMemberSig,
                    cMemberSig,
                    pMT->GetModule(),
                    pSignature,
                    cSignature,
                    pModule))
            {
                continue;
            }
        }

        return pFD;
    }

#if defined(FEATURE_METADATA_UPDATER) && !defined(DACCESS_COMPILE)
    if (pModule != NULL
        && pModule->IsFullModule()
        && ((Module*)pModule)->IsEditAndContinueEnabled())
    {
        LOG((LF_LOADER, LL_INFO100000, "ML::FF Falling back to EnC slow path\n"));

        // We may not have the full FieldDesc info at ApplyEnC time because we don't
        // have a thread so can't do things like load classes (due to possible exceptions)
        EncApproxFieldDescIterator fdIterator(
            pMT,
            ApproxFieldDescIterator::ALL_FIELDS,
            (EncApproxFieldDescIterator::FixUpEncFields | EncApproxFieldDescIterator::OnlyEncFields));
        PTR_FieldDesc pCurrentFD;
        while ((pCurrentFD = fdIterator.Next()) != NULL)
        {
            // Check is valid FieldDesc, and not some random memory
            INDEBUGIMPL(pCurrentFD->GetApproxEnclosingMethodTable()->SanityCheck());

            mdField = pCurrentFD->GetMemberDef();
            IfFailThrow(pInternalImport->GetNameOfFieldDef(mdField, &szMemberName));

            if (StrCompFunc(szMemberName, pszName) != 0)
                continue;

            if (pSignature != NULL)
            {
                PCCOR_SIGNATURE pMemberSig;
                DWORD       cMemberSig;
                IfFailThrow(pInternalImport->GetSigOfFieldDef(mdField, &cMemberSig, &pMemberSig));

                if (!MetaSig::CompareFieldSigs(
                        pMemberSig,
                        cMemberSig,
                        pMT->GetModule(),
                        pSignature,
                        cSignature,
                        pModule))
                {
                    continue;
                }
            }
            return pCurrentFD;
        }
    }
#endif // defined(FEATURE_METADATA_UPDATER) && !defined(DACCESS_COMPILE)

    return NULL;
}
